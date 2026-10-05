using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Shareme.Contracts.Knowledge;
using Shareme.Web.Client.Knowledge;

namespace Shareme.Web.Client.Pages;

public partial class People
{
    [Inject] private IPeopleApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<PersonDto> _people = [];
    private string? _search;
    private bool _editing;
    private ProfileForm _profile = new();

    [CascadingParameter] private Task<AuthenticationState>? Auth { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        if (Auth is not null && Guid.TryParse((await Auth).User.FindFirst("sub")?.Value ?? (await Auth).User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var me))
        {
            try
            {
                var own = await Api.GetAsync(me);
                _profile = new ProfileForm { Department = own.Department, Position = own.Position, Phone = own.Phone, Location = own.Location, About = own.About };
            }
            catch (ApiException)
            {
            }
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            _people = await Api.FindAsync(_search);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await Api.SaveProfileAsync(new ProfileRequest(_profile.Department, _profile.Position, _profile.Phone, _profile.Location, _profile.About));
            _editing = false;
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task UploadAsync(IBrowserFile? file)
    {
        if (file is null)
        {
            return;
        }

        try
        {
            await using var content = file.OpenReadStream(2L * 1024 * 1024);
            await Api.SetAvatarAsync(content);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is ApiException or IOException)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task RemoveAvatarAsync()
    {
        try
        {
            await Api.SetAvatarAsync(null);
            await LoadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }

    private async Task MoreAsync()
    {
        try
        {
            _people = [.. _people, .. await Api.FindAsync(_search, _people.Count)];
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Warning);
        }
    }

    private static string Initials(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    private sealed class ProfileForm
    {
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? Phone { get; set; }
        public string? Location { get; set; }
        public string? About { get; set; }
    }
}
