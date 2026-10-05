using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Automation;
using Shareme.Web.Client.Metadata;

namespace Shareme.Web.Client.Pages;

public partial class DataJobs : IDisposable
{
    [Inject] private IDataApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<DataJobDto> _jobs = [];
    private DataJobDto? _job;
    private Timer? _timer;

    [Parameter] public Guid? Id { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();

        // a running job is fetched again until it is done
        _timer?.Dispose();
        _timer = _job is { State: RuleJobStates.Queued or RuleJobStates.Running }
            ? new Timer(_ => InvokeAsync(async () =>
            {
                await LoadAsync();
                if (_job is not { State: RuleJobStates.Queued or RuleJobStates.Running })
                {
                    _timer?.Dispose();
                }

                StateHasChanged();
            }), null, 1000, 1000)
            : null;
    }

    private async Task LoadAsync()
    {
        try
        {
            _job = Id is { } id ? await Api.GetJobAsync(id) : null;
            _jobs = await Api.GetJobsAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private static string Kind(DataJobDto job) => job.Kind switch { DataJobKinds.Import => "Import", DataJobKinds.Extraction => "AI tagging", _ => "Mass edit" };

    public void Dispose() => _timer?.Dispose();
}
