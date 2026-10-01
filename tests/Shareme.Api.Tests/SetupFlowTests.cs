using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;

namespace Shareme.Api.Tests;

public sealed class SetupFlowTests(ApiFixture api)
{
    [Fact]
    public async Task Identity_api_is_gated_until_setup_then_admin_has_access()
    {
        var ct = TestContext.Current.CancellationToken;
        var anonymous = api.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/identity/users", ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var response = await anonymous.PostAsJsonAsync("/api/v1/setup/complete",
            new CompleteSetupRequest(ApiFixture.SetupToken, "Shareme", "admin@shareme.test", "Admin#12345", null, null), ct);
        response.EnsureSuccessStatusCode();
        var setup = (await response.Content.ReadFromJsonAsync<SetupResultDto>(ct))!;

        var admin = api.Factory.CreateClient().AsUser(setup.AdminUserId, setup.TenantId);
        (await admin.GetAsync("/api/v1/identity/users", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetStringAsync("/api/v1/settings/definitions", ct)).ShouldContain("Account.AllowRegistration");
        (await admin.PostAsync($"/api/v1/identity/users/{setup.AdminUserId}/password-reset", null, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
