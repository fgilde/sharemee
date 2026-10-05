using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Web.Client.Assistant;

namespace Shareme.Web.Client.Pages;

public partial class Assistant : IDisposable
{
    [Inject] private IAssistantApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    // the API takes at most 50 messages, 20000 characters each and 100000 in all
    private const int MaxHistory = 40;
    private const int MaxMessageLength = 20_000;
    private const int MaxHistoryLength = 90_000;

    private sealed record Turn(ChatMessageDto Message, IReadOnlyList<AiToolCallDto> ToolCalls);

    private readonly List<Turn> _turns = [];
    private readonly CancellationTokenSource _closed = new();
    private string? _input;
    private bool _busy;

    public void Dispose()
    {
        _closed.Cancel();
        _closed.Dispose();
    }

    private void Clear()
    {
        _turns.Clear();
        _input = null;
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(_input))
        {
            return;
        }

        _turns.Add(new Turn(new ChatMessageDto("user", Cut(_input.Trim())), []));
        _input = null;
        _busy = true;
        try
        {
            var answer = await Api.ChatAsync(History(), _closed.Token);
            _turns.Add(new Turn(new ChatMessageDto("assistant", string.IsNullOrWhiteSpace(answer.Text) ? "(no answer)" : Cut(answer.Text)), answer.ToolCalls));
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            // the page was left
        }
        catch (Exception exception)
        {
            _input = _turns[^1].Message.Text;
            _turns.RemoveAt(_turns.Count - 1);
            Snackbar.Add(exception is ApiException ? exception.Message : "The assistant did not answer in time. Try again or start a new conversation.", Severity.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>The API keeps no conversation: the newest turns that fit, starting with a question.</summary>
    private List<ChatMessageDto> History()
    {
        var history = new List<ChatMessageDto>();
        var length = 0;
        for (var i = _turns.Count - 1; i >= 0 && history.Count < MaxHistory; i--)
        {
            length += _turns[i].Message.Text.Length;
            if (length > MaxHistoryLength && history.Count > 0)
            {
                break;
            }

            history.Insert(0, _turns[i].Message);
        }

        return [.. history.SkipWhile(m => m.Role != "user")];
    }

    private static string Cut(string text) => text.Length > MaxMessageLength ? text[..MaxMessageLength] : text;
}
