using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Coworkee.Contracts.Identity;

namespace Shareme.AppHost.Tests;

public sealed partial class SignInFlowTests
{
    private const string SetupToken = "e2e-setup-token";

    [Fact]
    public async Task Admin_signs_in_through_the_bff_and_calls_the_api()
    {
        var ct = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shareme_AppHost>(
            [$"--{CoworkeeInfrastructureExtensions.EphemeralSetting}=true", $"--Shareme:SetupToken={SetupToken}"], ct);
        var app = await appHost.BuildAsync(ct);
        await using var stop = AppHostDiagnostics.Guard(app);
        await app.StartAsync(ct);
        await AppHostDiagnostics.WaitHealthyAsync(app, ["shareme-web", "shareme-auth", "shareme-api"], ct);

        var webBase = app.GetEndpoint("shareme-web", "https");
        var cookies = new CookieContainer();
        using var browser = new HttpClient(new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        }) { BaseAddress = webBase };

        using (var setup = new HttpRequestMessage(HttpMethod.Post, "/api/v1/setup/complete")
        {
            Content = JsonContent.Create(new CompleteSetupRequest(SetupToken, "Acme", "admin@acme.test", "Admin#12345", "Ada", "Admin")),
        })
        {
            setup.Headers.Add("X-CSRF", "1");
            (await browser.SendAsync(setup, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var loginPage = await browser.GetAsync("/bff/login?returnUrl=/bff/user", ct);
        var loginHtml = await loginPage.Content.ReadAsStringAsync(ct);
        loginHtml.ShouldContain("Sign in");
        var authorizeResponse = await browser.PostAsync(loginPage.RequestMessage!.RequestUri, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "admin@acme.test",
            ["Input.Password"] = "Admin#12345",
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(loginHtml).Groups[1].Value,
        }), ct);

        var formPost = await authorizeResponse.Content.ReadAsStringAsync(ct);
        var callback = await browser.PostAsync(FormAction().Match(formPost).Groups[1].Value,
            new FormUrlEncodedContent(HiddenField().Matches(formPost).ToDictionary(m => WebUtility.HtmlDecode(m.Groups[1].Value), m => WebUtility.HtmlDecode(m.Groups[2].Value))), ct);

        var user = JsonDocument.Parse(await callback.Content.ReadAsStringAsync(ct)).RootElement;
        user.GetProperty("isAuthenticated").GetBoolean().ShouldBeTrue();
        user.GetProperty("email").GetString().ShouldBe("admin@acme.test");

        var permissions = await browser.GetFromJsonAsync<string[]>("/api/v1/identity/permissions/me", ct);
        permissions.ShouldContain(IdentityPermissions.Users.Manage);

        using (var testMail = new HttpRequestMessage(HttpMethod.Post, "/api/v1/mail/templates/Identity.Welcome/en/test"))
        {
            testMail.Headers.Add("X-CSRF", "1");
            (await browser.SendAsync(testMail, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var mailpit = new HttpClient { BaseAddress = app.GetEndpoint("mail", "http") };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!(await mailpit.GetStringAsync("/api/v1/messages", ct)).Contains("admin@acme.test", StringComparison.OrdinalIgnoreCase))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "welcome test mail did not arrive");
            await Task.Delay(500, ct);
        }

        (await browser.GetAsync("/admin/jobs", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using (var hub = Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilderHttpExtensions.WithUrl(new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder(), new Uri(webBase, "hubs/realtime"), options =>
            {
                options.Cookies = cookies;
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = handler =>
                {
                    if (handler is HttpClientHandler client)
                    {
                        client.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    }

                    return handler;
                };
            })
            .Build())
        {
            Microsoft.AspNetCore.SignalR.Client.HubConnectionExtensions.On<JsonElement>(hub, "OnEvent", e => events.Enqueue(e.GetProperty("topic").GetString()!));
            await hub.StartAsync(ct);
            await Microsoft.AspNetCore.SignalR.Client.HubConnectionExtensions.InvokeAsync(hub, "Subscribe", "type:User", ct);
            await SendJsonAsync(browser, HttpMethod.Post, "/api/v1/identity/users", new { email = "live@acme.test", password = "Passw0rd!x" }, ct);
            var liveDeadline = DateTime.UtcNow.AddSeconds(30);
            while (!events.Contains("type:User"))
            {
                DateTime.UtcNow.ShouldBeLessThan(liveDeadline, "realtime event did not arrive through the bff");
                await Task.Delay(200, ct);
            }
        }

        (await browser.GetFromJsonAsync<JsonElement>("/api/v1/themes/current", ct)).GetProperty("name").GetString().ShouldBe("Coworkee");
        var palette = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" });
        var themeId = await SendJsonAsync(browser, HttpMethod.Post, "/api/v1/themes", new { name = "Brand", paletteLight = palette, paletteDark = palette }, ct);
        await SendJsonAsync(browser, HttpMethod.Put, $"/api/v1/themes/{themeId}", new { name = "Brand 2", paletteLight = palette, paletteDark = palette }, ct);
        (await browser.GetFromJsonAsync<JsonElement>($"/api/v1/versions/Theme/{themeId}", ct)).GetArrayLength().ShouldBe(2);
        (await browser.GetFromJsonAsync<JsonElement>($"/api/v1/audit?entityType=ThemeDefinition&entityId={themeId}", ct)).GetProperty("items").GetArrayLength().ShouldBe(2);

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logout.Headers.Add("X-CSRF", "1");
        var endSession = await (await browser.SendAsync(logout, ct)).Content.ReadFromJsonAsync<BffLogoutDto>(ct);
        (await browser.GetAsync(endSession!.Redirect, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await browser.GetFromJsonAsync<BffUserDto>("/bff/user", ct))!.IsAuthenticated.ShouldBeFalse();
        var reLogin = await browser.GetAsync("/bff/login?returnUrl=/bff/user", ct);
        reLogin.RequestMessage!.RequestUri!.AbsolutePath.ShouldBe("/Account/Login");

        var authBase = app.GetEndpoint("shareme-auth", "https");
        await PostAuthFormAsync(browser, new Uri(authBase, "/Account/ForgotPassword"), new() { ["Input.Email"] = "admin@acme.test" }, ct);
        string? resetLink = null;
        var resetDeadline = DateTime.UtcNow.AddSeconds(90);
        while (resetLink is null)
        {
            DateTime.UtcNow.ShouldBeLessThan(resetDeadline, "reset mail did not arrive");
            var messages = (await mailpit.GetFromJsonAsync<JsonElement>("/api/v1/messages", ct)).GetProperty("messages").EnumerateArray()
                .Where(m => m.GetProperty("Subject").GetString() == "Reset your password").ToList();
            if (messages.Count > 0)
            {
                var message = await mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/message/{messages[0].GetProperty("ID").GetString()}", ct);
                resetLink = WebUtility.HtmlDecode(ResetLink().Match(message.GetProperty("HTML").GetString()!).Groups[1].Value);
            }
            else
            {
                await Task.Delay(500, ct);
            }
        }

        (await PostAuthFormAsync(browser, new Uri(resetLink), new() { ["Input.Password"] = "Brand#New123", ["Input.ConfirmPassword"] = "Brand#New123" }, ct))
            .ShouldContain("Your password was changed");
        var newLogin = await browser.GetAsync("/bff/login?returnUrl=/bff/user", ct);
        var newLoginHtml = await newLogin.Content.ReadAsStringAsync(ct);
        var newAuthorize = await browser.PostAsync(newLogin.RequestMessage!.RequestUri, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "admin@acme.test",
            ["Input.Password"] = "Brand#New123",
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(newLoginHtml).Groups[1].Value,
        }), ct);
        var newFormPost = await newAuthorize.Content.ReadAsStringAsync(ct);
        var newCallback = await browser.PostAsync(FormAction().Match(newFormPost).Groups[1].Value,
            new FormUrlEncodedContent(HiddenField().Matches(newFormPost).ToDictionary(m => WebUtility.HtmlDecode(m.Groups[1].Value), m => WebUtility.HtmlDecode(m.Groups[2].Value))), ct);
        JsonDocument.Parse(await newCallback.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("isAuthenticated").GetBoolean().ShouldBeTrue();
    }

    private static async Task<string> SendJsonAsync(HttpClient client, HttpMethod method, string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF", "1");
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    private static async Task<string> PostAuthFormAsync(HttpClient browser, Uri url, Dictionary<string, string> fields, CancellationToken ct)
    {
        var page = await browser.GetStringAsync(url, ct);
        fields["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups[1].Value;
        var response = await browser.PostAsync(url, new FormUrlEncodedContent(fields), ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    [GeneratedRegex("href=\"([^\"]*/Account/ResetPassword[^\"]*)\"")]
    private static partial Regex ResetLink();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("<form[^>]*action=\"([^\"]+)\"")]
    private static partial Regex FormAction();

    [GeneratedRegex("<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")]
    private static partial Regex HiddenField();
}
