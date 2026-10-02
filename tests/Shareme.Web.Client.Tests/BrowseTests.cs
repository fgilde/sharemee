using Shareme.Web.Client.Assets;
using Bunit;
using Coworkee.Client.Blazor;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts;
using Coworkee.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using Shareme.Contracts.Graph;
using Shareme.Contracts.Metadata;
using Shareme.Web.Client.Graph;
using Shareme.Web.Client.Metadata;
using Shareme.Web.Client.Pages;

namespace Shareme.Web.Client.Tests;

public sealed class BrowseTests : BunitContext
{
    private readonly ISharemeApi _api = Substitute.For<ISharemeApi>();
    private readonly IDownloadsApi _downloads = Substitute.For<IDownloadsApi>();
    private readonly Shareme.Web.Client.Metadata.IDataApi _data = Substitute.For<Shareme.Web.Client.Metadata.IDataApi>();
    private readonly Guid _root = Guid.CreateVersion7();
    private readonly Guid _folder = Guid.CreateVersion7();
    private readonly Guid _child = Guid.CreateVersion7();

    public BrowseTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        MudBlazor.Extensions.ServiceCollectionExtensions.AddMudExtensions(Services);
        Services.AddSingleton(_downloads);
        Services.AddSingleton(_data);
        Services.AddSingleton(Substitute.For<Shareme.Web.Client.Metadata.IAiApi>());
        Services.AddSingleton(Substitute.For<Shareme.Web.Client.Workspaces.IWorkspacesApi>());
        Services.AddSingleton(_api);
        var metadata = Substitute.For<IMetadataApi>();
        metadata.GetNodeMetadataAsync(default, default).ReturnsForAnyArgs(new NodeMetadataDto(_folder, "folder", new Dictionary<string, EffectivePropertyDto>()));
        metadata.GetDefinitionsAsync(default, default).ReturnsForAnyArgs([]);
        metadata.GetGroupsAsync(default).ReturnsForAnyArgs([]);
        metadata.GetLanguagesAsync(default).ReturnsForAnyArgs([]);
        metadata.GetContextsAsync(default).ReturnsForAnyArgs([]);
        metadata.GetSetsAsync(default, default).ReturnsForAnyArgs([]);
        Services.AddSingleton(metadata);
        Services.AddScoped<Shareme.Web.Client.Assets.UploadQueue>();
        _api.GetEntryPointsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Services.AddSingleton(Substitute.For<ICoworkeeApi>());
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddSingleton<IRealtimeConnection>(Substitute.For<IRealtimeConnection>());
        Services.AddScoped<RealtimeClient>();
        AddAuthorization().SetAuthorized("Ada");
        _api.GetNodeAsync(_folder, Arg.Any<CancellationToken>()).Returns(new NodeDetailDto(Node(_folder, "Marketing", _root), [new(_root, "Root"), new(_folder, "Marketing")], false));
        _api.GetChildrenAsync(_folder, Arg.Any<PageRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<NodeDto>([Node(_child, "Campaigns", _folder)], 1, 1, 100));
    }

    [Fact]
    public void Shows_the_breadcrumb_and_the_children()
    {
        var page = Render<Browse>(p => p.Add(b => b.Id, _folder));

        page.WaitForAssertion(() => page.Find($"[data-node='{_child}']").TextContent.ShouldContain("Campaigns"));
        page.Find("[data-testid='breadcrumb']").TextContent.ShouldContain("Root");
    }

    [Fact]
    public async Task A_folder_downloads_as_zip_through_its_expiring_link()
    {
        _downloads.CreatePackageAsync(Arg.Any<Shareme.Contracts.Assets.DownloadPackageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Shareme.Contracts.Assets.DownloadPackageDto("/api/v1/downloads/abc", DateTimeOffset.UtcNow.AddHours(1), 3, 0));
        var page = Render<Browse>(p => p.Add(b => b.Id, _folder));

        await page.WaitForElement("[data-testid='download-folder']").ClickAsync(new());

        await _downloads.Received(1).CreatePackageAsync(Arg.Is<Shareme.Contracts.Assets.DownloadPackageRequest>(r => r.FolderId == _folder), Arg.Any<CancellationToken>());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri.ShouldEndWith("/api/v1/downloads/abc");
    }

    [Fact]
    public async Task Selected_nodes_are_exported_and_mass_edited()
    {
        _data.MassEditAsync(Arg.Any<Shareme.Contracts.Automation.MassEditRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Shareme.Contracts.Automation.DataJobDto(Guid.CreateVersion7(), "massEdit", "queued", false, 0, 0, 0, 0, null, [], DateTimeOffset.UtcNow));
        var page = Render<Browse>(p => p.Add(b => b.Id, _folder));
        page.WaitForElement($"input[data-testid='select-{_child}'], [data-testid='select-{_child}'] input").Change(true);

        await page.Find("[data-testid='export-selected']").ClickAsync(new());
        await _data.Received(1).ExportAsync(Arg.Is<Shareme.Contracts.Automation.ExportRequest>(r => r.NodeIds!.Single() == _child), Arg.Any<CancellationToken>());

        await page.Find("[data-testid='edit-selected']").ClickAsync(new());
        await page.WaitForElement("[data-testid='start-mass-edit']").ClickAsync(new());
        await _data.Received(1).MassEditAsync(Arg.Is<Shareme.Contracts.Automation.MassEditRequest>(r => r.NodeIds!.Single() == _child && r.Search == null), Arg.Any<CancellationToken>());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri.ShouldContain("/data-jobs/");
    }

    [Fact]
    public async Task Creating_a_folder_posts_it_under_the_current_folder()
    {
        _api.CreateNodeAsync(Arg.Any<CreateNodeRequest>(), Arg.Any<CancellationToken>()).Returns(Node(Guid.CreateVersion7(), "New", _folder));
        var page = Render<Browse>(p => p.Add(b => b.Id, _folder));
        page.WaitForElement($"[data-node='{_child}']");

        page.Find("input[data-testid='new-folder-name'], [data-testid='new-folder-name'] input").Input("Brand");
        await page.Find("[data-testid='create-folder']").ClickAsync(new());

        await _api.Received(1).CreateNodeAsync(Arg.Is<CreateNodeRequest>(r => r.ParentId == _folder && r.Name == "Brand"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_a_child_calls_the_api()
    {
        var page = Render<Browse>(p => p.Add(b => b.Id, _folder));

        await page.WaitForElement($"[data-testid='delete-{_child}']").ClickAsync(new());

        await _api.Received(1).DeleteNodeAsync(_child, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Children_reload_when_the_folder_changes_live()
    {
        var connection = new CapturingConnection();
        Services.AddSingleton<IRealtimeConnection>(connection);
        var page = Render<Browse>(p => p.Add(b => b.Id, _folder));
        page.WaitForElement($"[data-node='{_child}']");

        connection.Push($"children:{_folder}");

        page.WaitForAssertion(() => _api.Received(2).GetChildrenAsync(_folder, Arg.Any<PageRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task The_folder_picker_can_go_up_and_hides_the_moved_node()
    {
        _api.GetNodeAsync(_root, Arg.Any<CancellationToken>()).Returns(new NodeDetailDto(Node(_root, "Root", null), [new(_root, "Root")], false));
        _api.GetFoldersAsync(_folder, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<NodeDto>([], 0, 1, 500));
        _api.GetFoldersAsync(_root, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<NodeDto>([Node(_folder, "Marketing", _root), Node(_child, "Campaigns", _root)], 2, 1, 200));
        var picker = Render<FolderPicker>(p => p.Add(f => f.StartId, _folder).Add(f => f.ExcludeId, _child));

        await picker.WaitForElement("[data-testid='picker-crumb']").ClickAsync(new());

        picker.WaitForAssertion(() => picker.Markup.ShouldContain("Move here: Root"));
        picker.Markup.ShouldContain("Marketing");
        picker.Markup.ShouldNotContain("Campaigns");
    }

    private static NodeDto Node(Guid id, string name, Guid? parent) =>
        new(id, parent, NodeTypeKeys.Folder, "folder", name, new Dictionary<string, string>(), 0, false, DateTimeOffset.UtcNow, null);

    private sealed class CapturingConnection : IRealtimeConnection
    {
        private Action<RealtimeEnvelope>? _handler;

        public event Func<Task>? Reconnected;

        public Task StartAsync() => Task.CompletedTask;

        public Task InvokeAsync(string method, string topic) => Task.CompletedTask;

        public void OnEvent(Action<RealtimeEnvelope> handler) => _handler = handler;

        public void Push(string topic) => _handler?.Invoke(new RealtimeEnvelope(topic, RealtimeEventTypes.EntityChanged, System.Text.Json.JsonSerializer.SerializeToElement(new { })));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task ReconnectAsync() => Reconnected?.Invoke() ?? Task.CompletedTask;
    }
}
