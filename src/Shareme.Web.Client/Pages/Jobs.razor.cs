using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Automation;
using Shareme.Web.Client.Metadata;

namespace Shareme.Web.Client.Pages;

public partial class Jobs
{
    [Inject] private IDataApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private JobsOverviewDto? _overview;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _overview = await Api.GetOverviewAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private static string Kind(string kind) => kind switch
    {
        DataJobKinds.MassEdit => "Mass edit",
        DataJobKinds.Import => "Import",
        DataJobKinds.Extraction => "AI tagging",
        JobKinds.Rules => "Rule run",
        JobKinds.Archive => "Archive extraction",
        JobKinds.Reindex => "Search reindex",
        _ => kind,
    };
}
