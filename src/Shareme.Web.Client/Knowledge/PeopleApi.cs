using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Knowledge;

namespace Shareme.Web.Client.Knowledge;

public interface IPeopleApi
{
    Task<IReadOnlyList<PersonDto>> FindAsync(string? search, int skip = 0, int take = 50, CancellationToken cancellationToken = default);

    Task<PersonDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PersonDto> SaveProfileAsync(ProfileRequest request, CancellationToken cancellationToken = default);

    Task SetAvatarAsync(Stream? content, CancellationToken cancellationToken = default);

    Task<GlobalSearchDto> SearchEverythingAsync(string term, CancellationToken cancellationToken = default);

    static string AvatarUrl(Guid userId) => $"api/v1/people/{userId}/avatar";
}

internal sealed class PeopleApi(HttpClient http) : ApiClientBase(http), IPeopleApi
{
    public async Task<IReadOnlyList<PersonDto>> FindAsync(string? search, int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
        await GetAsync<PersonDto[]>($"api/v1/people?search={Uri.EscapeDataString(search ?? string.Empty)}&skip={skip}&take={take}", cancellationToken);

    public Task<PersonDto> GetAsync(Guid userId, CancellationToken cancellationToken = default) => GetAsync<PersonDto>($"api/v1/people/{userId}", cancellationToken);

    public Task<PersonDto> SaveProfileAsync(ProfileRequest request, CancellationToken cancellationToken = default) => SendAsync<PersonDto>(HttpMethod.Put, "api/v1/people/me", request, cancellationToken);

    public async Task SetAvatarAsync(Stream? content, CancellationToken cancellationToken = default)
    {
        if (content is null)
        {
            await SendAsync(HttpMethod.Delete, "api/v1/people/me/avatar", null, cancellationToken);
            return;
        }

        var body = new StreamContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendContentAsync(HttpMethod.Put, "api/v1/people/me/avatar", body, cancellationToken);
    }

    public Task<GlobalSearchDto> SearchEverythingAsync(string term, CancellationToken cancellationToken = default) =>
        GetAsync<GlobalSearchDto>($"api/v1/global-search?q={Uri.EscapeDataString(term)}", cancellationToken);
}
