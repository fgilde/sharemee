using Coworkee.Client.Blazor.Api;
using Shareme.Contracts.Workspaces;

namespace Shareme.Web.Client.Workspaces;

public interface IWorkspacesApi
{
    Task<IReadOnlyList<WorkspaceDto>> GetAsync(bool templates = false, CancellationToken cancellationToken = default);

    Task<WorkspaceDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkspaceDto> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(Guid id, UpdateWorkspaceRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceMemberDto>> GetMembersAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceMemberDto>> FindCandidatesAsync(Guid id, string? search, CancellationToken cancellationToken = default);

    Task AddMemberAsync(Guid id, AddWorkspaceMemberRequest request, CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceItemDto>> GetItemsAsync(Guid id, CancellationToken cancellationToken = default);

    Task UnlinkAsync(Guid id, Guid assetId, CancellationToken cancellationToken = default);

    Task LinkAsync(Guid id, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default);

    Task<FinalizeResultDto> FinalizeAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class WorkspacesApi(HttpClient http) : ApiClientBase(http), IWorkspacesApi
{
    private const string Base = "api/v1/workspaces";

    public async Task<IReadOnlyList<WorkspaceDto>> GetAsync(bool templates = false, CancellationToken cancellationToken = default) =>
        await GetAsync<WorkspaceDto[]>(Base + (templates ? "?templates=true" : string.Empty), cancellationToken);

    public Task<WorkspaceDto> GetAsync(Guid id, CancellationToken cancellationToken = default) => GetAsync<WorkspaceDto>($"{Base}/{id}", cancellationToken);

    public Task<WorkspaceDto> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default) => SendAsync<WorkspaceDto>(HttpMethod.Post, Base, request, cancellationToken);

    public Task UpdateAsync(Guid id, UpdateWorkspaceRequest request, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Put, $"{Base}/{id}", request, cancellationToken);

    public async Task<IReadOnlyList<WorkspaceMemberDto>> GetMembersAsync(Guid id, CancellationToken cancellationToken = default) =>
        await GetAsync<WorkspaceMemberDto[]>($"{Base}/{id}/members", cancellationToken);

    public async Task<IReadOnlyList<WorkspaceMemberDto>> FindCandidatesAsync(Guid id, string? search, CancellationToken cancellationToken = default) =>
        await GetAsync<WorkspaceMemberDto[]>($"{Base}/{id}/candidates?search={Uri.EscapeDataString(search ?? string.Empty)}", cancellationToken);

    public Task AddMemberAsync(Guid id, AddWorkspaceMemberRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Base}/{id}/members", request, cancellationToken);

    public Task RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"{Base}/{id}/members/{userId}", null, cancellationToken);

    public async Task<IReadOnlyList<WorkspaceItemDto>> GetItemsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await GetAsync<WorkspaceItemDto[]>($"{Base}/{id}/items", cancellationToken);

    public Task LinkAsync(Guid id, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{Base}/{id}/links", new LinkAssetsRequest(assetIds), cancellationToken);

    public Task UnlinkAsync(Guid id, Guid assetId, CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Delete, $"{Base}/{id}/links/{assetId}", null, cancellationToken);

    public Task<FinalizeResultDto> FinalizeAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<FinalizeResultDto>(HttpMethod.Post, $"{Base}/{id}/finalize", null, cancellationToken);
}
