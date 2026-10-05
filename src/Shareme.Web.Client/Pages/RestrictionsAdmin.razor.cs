using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Graph;
using Shareme.Web.Client.Graph;

namespace Shareme.Web.Client.Pages;

public partial class RestrictionsAdmin
{
    [Inject] private IRestrictionsApi Api { get; set; } = null!;

    [Inject] private ICoworkeeApi Identity { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<RestrictionDto> _restrictions = [];
    private IReadOnlyList<RoleDto> _roles = [];
    private Guid? _roleId;
    private string _kind = RestrictionKinds.HiddenProperty;
    private string? _propertyKey, _value;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        try
        {
            _roles = await Identity.GetRolesAsync();
        }
        catch (ApiException)
        {
        }
    }

    private static string Label(string kind) => kind switch
    {
        RestrictionKinds.HiddenProperty => "Hide property",
        RestrictionKinds.ReadOnlyProperty => "Property read only",
        RestrictionKinds.HiddenListValue => "Hide nodes with list value",
        _ => "Block rendition",
    };

    private async Task LoadAsync()
    {
        try
        {
            _restrictions = await Api.GetAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not manage restrictions." : exception.Message, Severity.Warning);
        }
    }

    private Task AddAsync() => RunAsync(async () =>
    {
        await Api.AddAsync(new RestrictionRequest(_roleId!.Value, _kind,
            _kind == RestrictionKinds.BlockedRendition ? null : _propertyKey,
            _kind is RestrictionKinds.HiddenProperty or RestrictionKinds.ReadOnlyProperty ? null : _value));
        _propertyKey = _value = null;
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
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
}
