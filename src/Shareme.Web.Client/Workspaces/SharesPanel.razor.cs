using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Assets;
using Shareme.Contracts.Workspaces;

namespace Shareme.Web.Client.Workspaces;

public partial class SharesPanel
{
    [Inject] private IWorkspacesApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<ShareDto> _shares = [];
    private string? _name, _emails;
    private DateTime? _until = DateTime.Today.AddDays(14);
    private IReadOnlyCollection<string> _kinds = [DerivationKinds.Preview];
    private bool _originals;

    [Parameter, EditorRequired] public Guid WorkspaceId { get; set; }

    /// <summary>Renditions that may be offered (system and the organisation's types).</summary>
    [Parameter] public IReadOnlyList<string> Kinds { get; set; } = [DerivationKinds.Thumbnail, DerivationKinds.Preview, DerivationKinds.Media];

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _shares = await Api.GetSharesAsync(WorkspaceId);
        }
        catch (ApiException)
        {
            _shares = [];
        }
    }

    private Task CreateAsync() => RunAsync(async () =>
    {
        var emails = (_emails ?? string.Empty).Split([',', ';', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var until = new DateTimeOffset(DateTime.SpecifyKind((_until ?? DateTime.Today.AddDays(14)).Date.AddDays(1).AddSeconds(-1), DateTimeKind.Local));
        await Api.CreateShareAsync(WorkspaceId, new ShareRequest(_name!.Trim(), until, emails, _originals, _kinds.ToList()));
        Snackbar.Add($"Links were sent to {emails.Length} addresses.", Severity.Success);
        (_name, _emails) = (null, null);
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }
}
