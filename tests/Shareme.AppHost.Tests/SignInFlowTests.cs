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
        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);
        foreach (var resource in new[] { "shareme-web", "shareme-auth", "shareme-api" })
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(resource, ct).WaitAsync(TimeSpan.FromMinutes(5), ct);
        }

        var webBase = app.GetEndpoint("shareme-web", "https");
        using var browser = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
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
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("<form[^>]*action=\"([^\"]+)\"")]
    private static partial Regex FormAction();

    [GeneratedRegex("<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")]
    private static partial Regex HiddenField();
}
