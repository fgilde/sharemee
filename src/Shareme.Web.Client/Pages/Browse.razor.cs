using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Graph;
using Shareme.Web.Client.Graph;

namespace Shareme.Web.Client.Pages;

public partial class Browse
{
    [Inject] private ISharemeApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Assets.IDownloadsApi Downloads { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Workspaces.IWorkspacesApi WorkspacesApi { get; set; } = null!;

    [Inject] private MudBlazor.Extensions.Services.MudExFileService Files { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Metadata.IDataApi Data { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Metadata.IAiApi Ai { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Drm.IDrmApi Drm { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Knowledge.IPagesApi Pages { get; set; } = null!;

    [Inject] private Shareme.Web.Client.Knowledge.IIntranetApi Intranet { get; set; } = null!;

    private NodeDetailDto? _detail;
    private IReadOnlyList<NodeDto>? _entryPoints;
    private IReadOnlyList<NodeDto> _children = [];
    private List<BreadcrumbItem> _breadcrumb = [];
    private string? _newName;
    private string? _search;
    private Guid? _renaming;
    private string? _renameValue;
    private List<NodeDto>? _moving;
    private readonly HashSet<Guid> _selected = [];
    private bool _massEdit;
    private bool? _importDryRun;
    private IReadOnlyList<Shareme.Contracts.Automation.ExtractionDefinitionDto> _extractions = [];
    private IReadOnlyList<Shareme.Contracts.Drm.LicenseDto> _licenses = [];

    private async Task LoadLicensesAsync()
    {
        try
        {
            _licenses = await Drm.GetLicensesAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not manage licenses." : exception.Message, Severity.Warning);
        }
    }

    private async Task AddToLicenseAsync(Guid license)
    {
        try
        {
            var updated = await Drm.AddAssetsAsync(license, SelectedAssets);
            Snackbar.Add($"{updated.Name} now covers {updated.AssetCount} files.", Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task LoadExtractionsAsync()
    {
        try
        {
            _extractions = await Ai.GetDefinitionsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not use AI tagging." : exception.Message, Severity.Warning);
        }
    }

    private async Task ExtractAsync(Guid extraction)
    {
        try
        {
            var job = await Ai.RunAsync(new Shareme.Contracts.Automation.ExtractionRunRequest(extraction, SelectedAssets));
            Nav.NavigateTo($"/data-jobs/{job.Id}");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
    private bool _gallery;

    [Parameter] public Guid? Id { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _detail = null;
        _entryPoints = null;
        _moving = null;
        _children = [];
        _search = null;
        _newName = null;
        _renaming = null;
        _selected.Clear();
        await RunAsync(async () =>
        {
            if (Id is { } id)
            {
                await LoadAsync(id);
                return;
            }

            await Api.GetRootAsync();
            var entries = await Api.GetEntryPointsAsync();
            if (entries.Count == 1)
            {
                Nav.NavigateTo($"/browse/{entries[0].Id}", replace: true);
            }
            else
            {
                _entryPoints = entries;
            }
        });
    }

    private async Task LoadAsync(Guid id)
    {
        _detail = await Api.GetNodeAsync(id);

        // files and pages have their own view
        if (_detail.Node.Semantic is "asset" or "page" or "news")
        {
            Nav.NavigateTo(Shareme.Web.Client.Knowledge.NodeLinks.For(_detail.Node), replace: true);
            return;
        }

        _breadcrumb = _detail.Breadcrumb
            .Select((crumb, index) => new BreadcrumbItem(crumb.Name, index == _detail.Breadcrumb.Count - 1 ? null : $"/browse/{crumb.Id}", disabled: index == _detail.Breadcrumb.Count - 1))
            .ToList();
        await LoadChildrenAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            await LoadAsync(_detail!.Node.Id);
        }
        catch (ApiException exception) when (exception.Status is 403 or 404)
        {
            Snackbar.Add("This folder is no longer available.", Severity.Warning);
            Nav.NavigateTo("/browse");
        }
    }

    private async Task LoadChildrenAsync()
    {
        if (_detail is null)
        {
            return;
        }

        _children = (await Api.GetChildrenAsync(_detail.Node.Id, new PageRequest(1, 200, _search))).Items;
        await InvokeAsync(StateHasChanged);
    }

    private Task CreateFolderAsync() => RunAsync(async () =>
    {
        await Api.CreateNodeAsync(new CreateNodeRequest(_detail!.Node.Id, _newName!.Trim()));
        _newName = null;
        await LoadChildrenAsync();
    });

    private void StartRename(NodeDto child)
    {
        _renaming = child.Id;
        _renameValue = child.Name;
    }

    private Task RenameAsync(NodeDto child) => RunAsync(async () =>
    {
        await Api.UpdateNodeAsync(child.Id, new UpdateNodeRequest(_renameValue ?? child.Name));
        _renaming = null;
        await LoadChildrenAsync();
    });

    private async Task MoveAsync(IReadOnlyList<NodeDto> nodes, Guid target)
    {
        await ForEachAsync(nodes.Where(n => n.Id != target).Select(n => n.Id), id => Api.MoveNodeAsync(id, target), "moved");
        _moving = null;
    }

    private IReadOnlyList<Shareme.Contracts.Workspaces.WorkspaceDto> _collections = [];

    private async Task LoadCollectionsAsync()
    {
        try
        {
            _collections = (await WorkspacesApi.GetAsync()).Where(w => w.Kind != Shareme.Contracts.Workspaces.WorkspaceKinds.Upload && w.MyRole is not null).ToList();
        }
        catch (ApiException)
        {
            _collections = [];
        }
    }

    private async Task LinkAsync(Guid collection)
    {
        try
        {
            await WorkspacesApi.LinkAsync(collection, SelectedAssets);
            Snackbar.Add($"{SelectedAssets.Count} assets added.", Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private List<Guid> SelectedAssets => _children.Where(c => c.Semantic == "asset" && _selected.Contains(c.Id)).Select(c => c.Id).ToList();

    /// <summary>The zip streams from an expiring link, so the browser shows its own progress.</summary>
    private async Task DownloadAsync(Shareme.Contracts.Assets.DownloadPackageRequest request)
    {
        try
        {
            var package = await Downloads.CreatePackageAsync(request);
            if (package.Skipped > 0)
            {
                Snackbar.Add($"{package.Skipped} files are left out (no download right or nothing to download).", Severity.Info);
            }

            Nav.NavigateTo(package.Url.TrimStart('/'), forceLoad: true);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task ContactSheetAsync(Shareme.Contracts.Assets.ContactSheetRequest request)
    {
        try
        {
            var pdf = await Downloads.ContactSheetAsync(request);
            await Files.DownloadContentAsync(pdf, "application/pdf", $"{request.Title ?? "contact"}-contact-sheet.pdf");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task ExportAsync(Shareme.Contracts.Automation.ExportRequest request, string name)
    {
        try
        {
            var content = await Data.ExportAsync(request);
            await Files.DownloadContentAsync(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}-metadata.xlsx");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task ImportAsync(Microsoft.AspNetCore.Components.Forms.IBrowserFile? file, bool dryRun)
    {
        if (file is null)
        {
            return;
        }

        try
        {
            await using var content = file.OpenReadStream(Shareme.Contracts.Automation.DataLimits.MaxImportBytes);
            var job = await Data.ImportAsync(content, _detail!.Node.Id, dryRun);
            Nav.NavigateTo($"/data-jobs/{job.Id}");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private Task DeleteSelectedAsync() => ForEachAsync(_selected.ToList(), id => Api.DeleteNodeAsync(id), "moved to the recycle bin");

    /// <summary>Runs one call per item, keeps going on failures and reports both counts.</summary>
    private async Task ForEachAsync(IEnumerable<Guid> ids, Func<Guid, Task> action, string done)
    {
        var (succeeded, failed) = (0, 0);
        foreach (var id in ids)
        {
            try
            {
                await action(id);
                succeeded++;
            }
            catch (ApiException)
            {
                failed++;
            }
        }

        Snackbar.Add($"{succeeded} items {done}." + (failed > 0 ? $" {failed} could not be changed." : string.Empty), failed > 0 ? Severity.Warning : Severity.Info);
        _selected.Clear();
        await LoadChildrenAsync();
    }

    private void Toggle(Guid id, bool on)
    {
        if (on)
        {
            _selected.Add(id);
        }
        else
        {
            _selected.Remove(id);
        }
    }

    private static string Link(NodeDto node) => Shareme.Web.Client.Knowledge.NodeLinks.For(node);

    private static string Icon(NodeDto node) => Shareme.Web.Client.Knowledge.NodeLinks.Icon(node.Semantic);

    private async Task CreateNewsAsync()
    {
        try
        {
            // a draft: it goes live when its author publishes it
            var news = await Intranet.CreateNewsAsync(new Shareme.Contracts.Knowledge.NewsRequest(_detail!.Node.Id, _newName!.Trim(), string.Empty, DateTimeOffset.UtcNow.AddYears(100)));
            _newName = null;
            Nav.NavigateTo($"/news/{news.Id}?edit=true");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task CreatePageAsync()
    {
        try
        {
            var page = await Pages.CreateAsync(new Shareme.Contracts.Knowledge.PageRequest(_detail!.Node.Id, _newName!.Trim(), $"# {_newName!.Trim()}\n"));
            _newName = null;
            Nav.NavigateTo($"/pages/{page.Id}");
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private Task DeleteAsync(NodeDto child) => RunAsync(async () =>
    {
        await Api.DeleteNodeAsync(child.Id);
        Snackbar.Add($"\"{child.Name}\" moved to the recycle bin.", Severity.Info);
        await LoadChildrenAsync();
    });

    private Task ToggleSubscriptionAsync() => RunAsync(async () =>
    {
        await Api.SetSubscriptionAsync(_detail!.Node.Id, !_detail.IsSubscribed);
        _detail = _detail with { IsSubscribed = !_detail.IsSubscribed };
    });

    private Task SetInheritanceAsync(bool interrupt) => RunAsync(async () =>
    {
        await Api.SetInheritanceAsync(_detail!.Node.Id, interrupt);
        _detail = _detail with { Node = _detail.Node with { InterruptInheritance = interrupt } };
    });

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException exception) when (exception.Status == 403)
        {
            Snackbar.Add("You do not have permission for this.", Severity.Warning);
        }
        catch (ApiException exception) when (exception.Status == 404)
        {
            Snackbar.Add("This folder no longer exists.", Severity.Warning);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : exception.Message, Severity.Error);
        }
    }
}
