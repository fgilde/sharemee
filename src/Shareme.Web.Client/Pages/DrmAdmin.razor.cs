using Coworkee.Client.Blazor.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Shareme.Contracts.Drm;
using Shareme.Web.Client.Drm;

namespace Shareme.Web.Client.Pages;

public partial class DrmAdmin
{
    [Inject] private IDrmApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private const string License = "licenses", Owners = "rights-owners", Contacts = "billing-contacts", PriceLists = "price-lists", Groups = "context-groups";
    private IReadOnlyList<LicenseDto> _licenses = [];
    private IReadOnlyList<RightsOwnerDto> _owners = [];
    private IReadOnlyList<BillingContactDto> _contacts = [];
    private IReadOnlyList<PriceListDto> _priceLists = [];
    private IReadOnlyList<ContextGroupDto> _groups = [];
    private IReadOnlyList<BuyoutDto> _buyouts = [];
    private Guid? _licenseId, _ownerContact;
    private LicenseForm _license = new();
    private BuyoutForm _buyout = new();
    private string? _ownerName, _ownerKind, _contactName, _contactEmail, _contactIban, _priceListName, _priceListEntries, _groupName, _groupContexts;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _licenses = await Api.GetLicensesAsync();
            _owners = await Api.GetOwnersAsync();
            _contacts = await Api.GetContactsAsync();
            _priceLists = await Api.GetPriceListsAsync();
            _groups = await Api.GetGroupsAsync();
            _buyouts = _licenseId is { } id ? await Api.GetBuyoutsAsync(id) : [];
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Status == 403 ? "You may not manage licenses." : exception.Message, Severity.Warning);
        }
    }

    private static IReadOnlyList<string> Split(string? text) => (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task EditLicenseAsync(LicenseDto license)
    {
        _licenseId = license.Id;
        _license = new LicenseForm
        {
            Name = license.Name, Description = license.Description, Rights = string.Join(", ", license.Rights), Days = license.PurchaseDays, Status = license.Status,
            From = license.ValidFrom?.ToDateTime(TimeOnly.MinValue), Until = license.ValidUntil?.ToDateTime(TimeOnly.MinValue),
        };
        await LoadAsync();
    }

    private Task SaveLicenseAsync() => RunAsync(async () =>
    {
        var saved = await Api.SaveLicenseAsync(_licenseId, new LicenseRequest(_license.Name ?? string.Empty, _license.Description, Split(_license.Rights),
            _license.From is { } from ? DateOnly.FromDateTime(from) : null, _license.Until is { } until ? DateOnly.FromDateTime(until) : null, _license.Days, _license.Status));
        _licenseId = saved.Id;
    });

    private Task AddBuyoutAsync(Guid licenseId) => RunAsync(async () =>
    {
        await Api.SaveBuyoutAsync(licenseId, null, new BuyoutRequest(_buyout.OwnerId!.Value, _buyout.PriceListId!.Value, _buyout.Currency, _buyout.Basis, _buyout.Fee));
        _buyout = new();
    });

    /// <summary>Lines like "dach=120" for a context key or "Europe=80" for a group name.</summary>
    private Task AddPriceListAsync() => RunAsync(async () =>
    {
        var entries = new List<PriceListEntry>();
        foreach (var line in (_priceListEntries ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && decimal.TryParse(parts[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var percentage))
            {
                entries.Add(_groups.FirstOrDefault(g => g.Name == parts[0]) is { } group ? new PriceListEntry(null, group.Id, percentage) : new PriceListEntry(parts[0], null, percentage));
            }
        }

        await Api.SavePriceListAsync(null, new PriceListRequest(_priceListName ?? string.Empty, Entries: entries));
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

    private sealed class LicenseForm
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Rights { get; set; }
        public DateTime? From { get; set; }
        public DateTime? Until { get; set; }
        public int Days { get; set; } = 365;
        public string Status { get; set; } = LicenseStatuses.Draft;
    }

    private sealed class BuyoutForm
    {
        public Guid? OwnerId { get; set; }
        public Guid? PriceListId { get; set; }
        public decimal Basis { get; set; }
        public string Currency { get; set; } = "EUR";
        public decimal Fee { get; set; }
    }
}
