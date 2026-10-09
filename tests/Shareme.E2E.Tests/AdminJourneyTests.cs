using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace Shareme.E2E.Tests;

public sealed class AdminJourneyTests
{
    private const string SetupToken = "e2e-browser-token";
    private const string AdminEmail = "admin@acme.test";
    private const string AdminPassword = "Admin#12345";
    private static readonly float Timeout = (float)TimeSpan.FromSeconds(60).TotalMilliseconds;
    private readonly List<IPage> _pages = [];

    [Fact]
    public async Task Setup_sign_in_create_user_assign_role_and_a_second_browser_sees_it_live()
    {
        try
        {
            await JourneyAsync();
        }
        catch (TimeoutException) when (!_policyViolations.IsEmpty)
        {
            throw new InvalidOperationException("Blocked by the content security policy: " + string.Join(Environment.NewLine, _policyViolations));
        }

    }

    private async Task JourneyAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Shareme_AppHost>(
            [$"--{CoworkeeAppExtensions.EphemeralSetting}=true", $"--Coworkee:SetupToken={SetupToken}"], ct);
        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);
        foreach (var resource in new[] { "shareme-web", "shareme-auth", "shareme-api" })
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(resource, ct).WaitAsync(TimeSpan.FromMinutes(5), ct);
        }

        var web = app.GetEndpoint("shareme-web", "https").ToString().TrimEnd('/');
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        try
        {

            var first = await NewPageAsync(browser);
            await first.GotoAsync(web + "/");
            await first.WaitForURLAsync("**/setup", new() { Timeout = Timeout });
            await first.Locator("input[data-testid='setup-token']").FillAsync(SetupToken);
            await first.Locator("[data-testid='next']").ClickAsync();
            await first.Locator("[data-check='Database']").WaitForAsync(new() { Timeout = Timeout });
            await first.Locator("[data-testid='next']").ClickAsync();
            await first.Locator("input[data-testid='tenant-name']").FillAsync("Acme");
            await first.Locator("[data-testid='next']").ClickAsync();

            // the look step applies a theme at once; setup shows no navigation
            await first.Locator("[data-theme='Ocean']").ClickAsync(new() { Timeout = Timeout });
            await ShotAsync(first, "setup-look-ocean");
            await first.Locator("[data-mode='dark']").ClickAsync();
            await ShotAsync(first, "setup-look-ocean-dark");
            await first.Locator("[data-mode='light']").ClickAsync();
            (await first.Locator(".mud-nav-menu").CountAsync()).ShouldBe(0);
            await first.Locator("[data-testid='next']").ClickAsync();
            await first.Locator("input[data-testid='admin-email']").FillAsync(AdminEmail);
            await first.Locator("input[data-testid='admin-password']").FillAsync(AdminPassword);
            await first.Locator("input[data-testid='admin-password-confirm']").FillAsync(AdminPassword);
            await ShotAsync(first, "setup-admin");
            await first.Locator("[data-testid='next']").ClickAsync();
            await first.Locator("[data-testid='finish']").ClickAsync();
            await first.Locator("[data-testid='sign-in']").WaitForAsync(new() { Timeout = Timeout });
            await ShotAsync(first, "setup-done");
            await first.Locator("[data-testid='sign-in']").ClickAsync(new() { Timeout = Timeout });

            // the sign-in form starts with the administrator's address
            (await first.Locator("#email").InputValueAsync()).ShouldBe(AdminEmail);
            await SignInAsync(first);
            await first.WaitForURLAsync(web + "/", new() { Timeout = Timeout });
            // the generated app configuration is editable by the system admin
            await first.GotoAsync(web + "/admin/settings");
            await first.GetByRole(AriaRole.Tab, new() { Name = "Sharemee" }).ClickAsync(new() { Timeout = Timeout });
            await first.Locator("[data-section='Shareme']").GetByText("Max nodes").WaitForAsync(new() { Timeout = Timeout });
            await ShotAsync(first, "configuration");

            // the own profile, and a user's page from the list
            await first.GotoAsync(web + "/profile");
            await first.Locator("[data-testid='profile-card']").WaitForAsync(new() { Timeout = Timeout });
            await ShotAsync(first, "profile");
            await first.GotoAsync(web + "/admin/users");
            await first.GetByRole(AriaRole.Link, new() { Name = AdminEmail }).ClickAsync(new() { Timeout = Timeout });
            await first.Locator("[data-testid='user-name']").WaitForAsync(new() { Timeout = Timeout });
            await ShotAsync(first, "user-detail");
            await first.GotoAsync(web + "/admin/users");

            await first.GotoAsync(web + "/admin/roles");
            await first.Locator("[data-testid='create']").ClickAsync(new() { Timeout = Timeout });
            await first.Locator(".mud-dialog input:not([placeholder='Filter'])").First.FillAsync("Editors");
            await first.Locator(".mud-dialog").GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            await first.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/admin/roles/[0-9a-f-]{36}$"), new() { Timeout = Timeout });
            await first.GotoAsync(web + "/admin/roles");
            await first.GetByRole(AriaRole.Link, new() { Name = "Editors" }).WaitForAsync(new() { Timeout = Timeout });

            var second = await NewPageAsync(browser);
            await second.GotoAsync(web + "/bff/login?returnUrl=/admin/users");
            await SignInAsync(second);
            await second.WaitForURLAsync("**/admin/users", new() { Timeout = Timeout });
            await second.Locator($"[data-user='{AdminEmail}']").WaitForAsync(new() { Timeout = Timeout });

            await first.GotoAsync(web + "/admin/users");
            await first.Locator("[data-testid='create']").ClickAsync(new() { Timeout = Timeout });
            await first.Locator("input[data-testid='new-user-email']").FillAsync("eve@acme.test");
            await first.Locator("input[data-testid='new-user-password']").FillAsync("Passw0rd!x");
            await first.Locator("[data-testid='create-user']").ClickAsync();

            await second.Locator("[data-user='eve@acme.test']").WaitForAsync(new() { Timeout = Timeout });

            await first.Locator("[data-testid='roles-eve@acme.test']").ClickAsync();
            await first.Locator(".mud-popover-open .mud-list-item", new() { HasTextString = "Editors" }).ClickAsync();
            await first.Keyboard.PressAsync("Escape");

            await Assertions.Expect(second.Locator("[data-testid='roles-eve@acme.test'] input, input[data-testid='roles-eve@acme.test']").First)
                .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("Editors"), new() { Timeout = Timeout });

            await first.GotoAsync(web + "/browse");
            await first.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/browse/[0-9a-f-]{36}$"), new() { Timeout = Timeout });
            var rootUrl = first.Url;
            await second.GotoAsync(rootUrl);
            await second.Locator("[data-testid='create-folder']").WaitForAsync(new() { Timeout = Timeout });

            await first.Locator("input[data-testid='new-folder-name'], [data-testid='new-folder-name'] input").First.FillAsync("Brand assets");
            await first.Locator("[data-testid='create-folder']").ClickAsync();

            var liveRow = second.Locator("tr[data-node]", new() { HasTextString = "Brand assets" });
            await liveRow.WaitForAsync(new() { Timeout = Timeout });

            var row = first.Locator("tr[data-node]", new() { HasTextString = "Brand assets" });
            await row.Locator("[data-testid^='delete-']").ClickAsync();
            await Assertions.Expect(liveRow).ToHaveCountAsync(0, new() { Timeout = Timeout });

            await first.GotoAsync(web + "/recycle-bin");
            await first.Locator("tr[data-item]", new() { HasTextString = "Brand assets" }).Locator("[data-testid^='restore-']").ClickAsync();
            await second.Locator("tr[data-node]", new() { HasTextString = "Brand assets" }).WaitForAsync(new() { Timeout = Timeout });

            await first.GotoAsync(web + "/admin/metadata");
            await first.Locator("input[data-testid='definition-key'], [data-testid='definition-key'] input").First.FillAsync("client");
            await first.Locator("input[data-testid='definition-name'], [data-testid='definition-name'] input").First.FillAsync("Client");
            await first.Locator("[data-testid='save-definition']").ClickAsync();
            await ShotAsync(first, "metadata-definitions");
            await first.Locator("tr[data-definition='client']").WaitForAsync(new() { Timeout = Timeout });

            await first.GotoAsync(rootUrl);
            var clientField = first.Locator("[data-property='client'] input").First;
            await clientField.FillAsync("Acme");
            await first.Locator("[data-testid='save-metadata']").ClickAsync();
            await first.GetByText("Metadata saved.").WaitForAsync(new() { Timeout = Timeout });
            await first.ReloadAsync();
            await Assertions.Expect(first.Locator("[data-property='client'] input").First).ToHaveValueAsync("Acme", new() { Timeout = Timeout });

            // upload a picture, the second browser sees it live, the processing host renders its preview
            await first.Locator("input[type=file]").First.SetInputFilesAsync(new FilePayload
            {
                Name = "pixel.png",
                MimeType = "image/png",
                Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="),
            });
            await first.Locator("[data-upload-state='done']").WaitForAsync(new() { Timeout = Timeout });
            await second.Locator("tr[data-node]", new() { HasTextString = "pixel.png" }).WaitForAsync(new() { Timeout = Timeout });

            await first.Locator("tr[data-node]", new() { HasTextString = "pixel.png" }).GetByRole(AriaRole.Link, new() { Name = "pixel.png" }).ClickAsync();
            await first.Locator("[data-testid='asset-status']").WaitForAsync(new() { Timeout = Timeout });
            // MudExFileDisplay shows images in its zoomable canvas viewer
            await first.Locator("[data-testid='preview'] .MudExImageView-container canvas").First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 60_000 });

            // indexing runs in the background, so the search is repeated until the upload shows up
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (true)
            {
                await first.GotoAsync(web + "/search?q=pixel");
                await first.Locator("[data-testid='total']").WaitForAsync(new() { Timeout = Timeout });
                if (await first.Locator("tr[data-hit]", new() { HasTextString = "pixel.png" }).CountAsync() > 0)
                {
                    break;
                }

                DateTime.UtcNow.ShouldBeLessThan(deadline, "the upload was not found by the search");
                await first.WaitForTimeoutAsync(1000);
            }

            foreach (var path in new[] { "/", "/people", "/find?q=pixel", "/assistant", "/admin/ai-tool-calls", "/jobs" })
            {
                await first.GotoAsync(web + path);
                await first.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            _policyViolations.ShouldBeEmpty();
        }
        catch (TimeoutException)
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                await ShotAsync(_pages[i], $"failure-{i}");
            }

            throw;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _policyViolations = new();

    private async Task<IPage> NewPageAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "en-US" });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(Timeout);

        // the content security policy must not block anything the app does
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
            {
                _policyViolations.Enqueue(message.Text);
            }
        };
        _pages.Add(page);
        return page;
    }

    /// <summary>With SHAREME_E2E_SCREENSHOTS set, keeps a picture of the page there (to look at a design without clicking through).</summary>
    private static async Task ShotAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("SHAREME_E2E_SCREENSHOTS") is { Length: > 0 } folder)
        {
            await page.Mouse.MoveAsync(1, 1);
            await page.WaitForTimeoutAsync(1000); // transitions settle
            await page.ScreenshotAsync(new() { Path = Path.Combine(folder, name + ".png"), FullPage = true });
        }
    }

    private static async Task SignInAsync(IPage page)
    {
        await page.Locator("#email").FillAsync(AdminEmail);
        await page.Locator("#password").FillAsync(AdminPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
    }
}
