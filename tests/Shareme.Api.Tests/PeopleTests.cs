using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Testing;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Knowledge;

namespace Shareme.Api.Tests;

public sealed class PeopleTests(ApiFixture api) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await api.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => api.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task Members_find_each_other_and_keep_their_own_profile_and_avatar()
    {
        var grace = await UserAsync("Grace", "Hopper");
        var alan = await UserAsync("Alan", "Turing");
        var asGrace = api.As(grace, _setup.TenantId);
        var profile = await (await asGrace.PutAsJsonAsync("/api/v1/people/me", new ProfileRequest("Research", "Rear admiral", "+1 555", "Arlington", "Compilers."), Ct)).Content.ReadFromJsonAsync<PersonDto>(Ct);
        (profile!.DisplayName, profile.Department).ShouldBe(("Grace Hopper", "Research"));

        var asAlan = api.As(alan, _setup.TenantId);
        (await asAlan.GetFromJsonAsync<PersonDto[]>("/api/v1/people?search=research", Ct))!.Single().UserId.ShouldBe(grace);
        (await asAlan.GetFromJsonAsync<PersonDto[]>("/api/v1/people?search=turing", Ct))!.Single().UserId.ShouldBe(alan);
        (await asAlan.GetFromJsonAsync<PersonDto[]>("/api/v1/people", Ct))!.Length.ShouldBeGreaterThanOrEqualTo(3);
        (await asAlan.GetFromJsonAsync<PersonDto>($"/api/v1/people/{grace}", Ct))!.Location.ShouldBe("Arlington");

        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Navy, 32, 32);
        (await asGrace.PutAsync("/api/v1/people/me/avatar", new ByteArrayContent(image.ToByteArray(ImageMagick.MagickFormat.Png)), Ct)).EnsureSuccessStatusCode();
        (await asGrace.PutAsync("/api/v1/people/me/avatar", new StringContent("<svg onload=alert(1)>"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var avatar = await asAlan.GetAsync($"/api/v1/people/{grace}/avatar", Ct);
        avatar.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await asAlan.GetFromJsonAsync<PersonDto>($"/api/v1/people/{grace}", Ct))!.HasAvatar.ShouldBeTrue();
        (await asAlan.GetAsync($"/api/v1/people/{alan}/avatar", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await asGrace.PutAsJsonAsync("/api/v1/people/me", new ProfileRequest(About: new string('x', 2001)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await asAlan.GetAsync($"/api/v1/people/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Global_search_finds_nodes_and_people_together()
    {
        var root = (await Admin.GetFromJsonAsync<NodeDto>("/api/v1/nodes/root", Ct))!.Id;
        var folder = (await (await Admin.PostAsJsonAsync("/api/v1/nodes", new CreateNodeRequest(root, "Hopper archive"), Ct)).Content.ReadFromJsonAsync<NodeDto>(Ct))!.Id;
        var grace = await UserAsync("Grace", "Hopper");

        var deadline = DateTime.UtcNow.AddSeconds(60);
        GlobalSearchDto found;
        while ((found = (await Admin.GetFromJsonAsync<GlobalSearchDto>("/api/v1/global-search?q=hopper", Ct))!).Nodes.All(n => n.Id != folder))
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the folder was not found");
            await Task.Delay(500, Ct);
        }

        found.People.Single().UserId.ShouldBe(grace);
        (await Admin.GetAsync("/api/v1/global-search?q=", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> UserAsync(string first, string last) =>
        (await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"{Guid.NewGuid():N}@acme.test", "Passw0rd!x", first, last), Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
}
