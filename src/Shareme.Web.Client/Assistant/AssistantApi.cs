using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Coworkee.Contracts.Ai;

namespace Shareme.Web.Client.Assistant;

public interface IAssistantApi
{
    Task<ChatResponseDto> ChatAsync(IReadOnlyList<ChatMessageDto> messages, CancellationToken cancellationToken = default);

    Task<PagedResult<AiToolCallDto>> GetToolCallsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
}

internal sealed class AssistantApi(HttpClient http) : ApiClientBase(http), IAssistantApi
{
    public Task<ChatResponseDto> ChatAsync(IReadOnlyList<ChatMessageDto> messages, CancellationToken cancellationToken = default) =>
        SendAsync<ChatResponseDto>(HttpMethod.Post, "api/v1/ai/chat", new ChatRequest(messages), cancellationToken);

    public Task<PagedResult<AiToolCallDto>> GetToolCallsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<AiToolCallDto>>($"api/v1/ai/tool-calls?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? string.Empty)}", cancellationToken);
}
