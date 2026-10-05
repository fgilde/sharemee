using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;

namespace Shareme.Web.Client.Pages;

public partial class Find
{
    [Inject] private IPeopleApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private GlobalSearchDto? _result;
    private string? _term;

    [SupplyParameterFromQuery(Name = "q")] public string? Term { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _term = Term;
        _result = null;
        if (!string.IsNullOrWhiteSpace(Term))
        {
            try
            {
                _result = await Api.SearchEverythingAsync(Term.Trim());
            }
            catch (ApiException exception)
            {
                Snackbar.Add(exception.Message, Severity.Warning);
            }
        }
    }

    private Task KeyAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && !string.IsNullOrWhiteSpace(_term))
        {
            Nav.NavigateTo($"/find?q={Uri.EscapeDataString(_term.Trim())}");
        }

        return Task.CompletedTask;
    }
}
