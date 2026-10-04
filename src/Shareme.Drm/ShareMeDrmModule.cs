using System.Text.Json;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Core.Modularity;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Mailing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Shareme.Assets;
using Shareme.Contracts.Drm;

namespace Shareme.Drm;

/// <summary>Licenses of assets, the rights owners behind them, their prices per context, offers and purchases with mails.</summary>
[DependsOn(typeof(ShareMeAssetsModule), typeof(CoworkeeMailingModule))]
public sealed class ShareMeDrmModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(ShareMeDrmModule).Assembly);
        context.Services.AddSingleton<IModelContributor, DrmModelContributor>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, DrmPermissionDefinitions>();
        context.Services.AddSingleton<IMailTemplateContributor, DrmMailTemplates>();
        context.Services.AddScoped<Offers>();
        context.Services.AddScoped<Shareme.Graph.INodePurgeListener, LicenseAssetPurgeListener>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var drm = app.MapGroup("/api/v1/drm").WithTags("DRM").RequireAuthorization();
        drm.MapGet("/billing-contacts", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetBillingContacts(), ct).ToHttpResult());
        drm.MapPost("/billing-contacts", (BillingContactRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveBillingContact(null, body), ct).ToHttpResult());
        drm.MapPut("/billing-contacts/{id:guid}", (Guid id, BillingContactRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveBillingContact(id, body), ct).ToHttpResult());
        drm.MapDelete("/billing-contacts/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDrmObject(DrmObjects.BillingContact, id), ct).ToHttpResult());

        drm.MapGet("/rights-owners", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRightsOwners(), ct).ToHttpResult());
        drm.MapPost("/rights-owners", (RightsOwnerRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveRightsOwner(null, body), ct).ToHttpResult());
        drm.MapPut("/rights-owners/{id:guid}", (Guid id, RightsOwnerRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveRightsOwner(id, body), ct).ToHttpResult());
        drm.MapDelete("/rights-owners/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDrmObject(DrmObjects.RightsOwner, id), ct).ToHttpResult());

        drm.MapGet("/context-groups", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetContextGroups(), ct).ToHttpResult());
        drm.MapPost("/context-groups", (ContextGroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveContextGroup(null, body), ct).ToHttpResult());
        drm.MapPut("/context-groups/{id:guid}", (Guid id, ContextGroupRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveContextGroup(id, body), ct).ToHttpResult());
        drm.MapDelete("/context-groups/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDrmObject(DrmObjects.ContextGroup, id), ct).ToHttpResult());

        drm.MapGet("/price-lists", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPriceLists(), ct).ToHttpResult());
        drm.MapPost("/price-lists", (PriceListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePriceList(null, body), ct).ToHttpResult());
        drm.MapPut("/price-lists/{id:guid}", (Guid id, PriceListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SavePriceList(id, body), ct).ToHttpResult());
        drm.MapDelete("/price-lists/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDrmObject(DrmObjects.PriceList, id), ct).ToHttpResult());

        drm.MapGet("/licenses", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetLicenses(), ct).ToHttpResult());
        drm.MapPost("/licenses", (LicenseRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveLicense(null, body), ct).ToHttpResult());
        drm.MapPut("/licenses/{id:guid}", (Guid id, LicenseRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveLicense(id, body), ct).ToHttpResult());
        drm.MapDelete("/licenses/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDrmObject(DrmObjects.License, id), ct).ToHttpResult());
        drm.MapGet("/licenses/{id:guid}/assets", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetLicenseAssets(id), ct).ToHttpResult());
        drm.MapPost("/licenses/{id:guid}/assets", (Guid id, LicenseAssetsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new ChangeLicenseAssets(id, body.AssetIds, true), ct).ToHttpResult());
        drm.MapPost("/licenses/{id:guid}/assets/remove", (Guid id, LicenseAssetsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new ChangeLicenseAssets(id, body.AssetIds, false), ct).ToHttpResult());
        drm.MapGet("/licenses/{id:guid}/buyouts", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetBuyouts(id), ct).ToHttpResult());
        drm.MapPost("/licenses/{id:guid}/buyouts", (Guid id, BuyoutRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveBuyout(id, null, body), ct).ToHttpResult());
        drm.MapPut("/licenses/{id:guid}/buyouts/{buyoutId:guid}", (Guid id, Guid buyoutId, BuyoutRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SaveBuyout(id, buyoutId, body), ct).ToHttpResult());
        drm.MapDelete("/licenses/{id:guid}/buyouts/{buyoutId:guid}", (Guid id, Guid buyoutId, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteDrmObject(DrmObjects.Buyout, buyoutId, id), ct).ToHttpResult());
        drm.MapGet("/assets/{assetId:guid}/licenses", (Guid assetId, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAssetLicenses(assetId), ct).ToHttpResult());

        drm.MapPost("/offers/search", (OfferSearchRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SearchOffers(body), ct).ToHttpResult());
        drm.MapPost("/purchases", (PurchaseRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreatePurchase(body), ct).ToHttpResult());
        drm.MapGet("/purchases", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPurchases(), ct).ToHttpResult());
        drm.MapGet("/purchases/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetPurchase(id), ct).ToHttpResult());
    }
}

public sealed class BillingContact : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public required string Email { get; set; }

    public string? Address { get; set; }

    public string? Iban { get; set; }

    public string? Bic { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid TenantId { get; set; }
}

public sealed class RightsOwner : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public string? Kind { get; set; }

    public Guid? BillingContactId { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid TenantId { get; set; }
}

public sealed class ContextGroup : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public List<string> ContextKeys { get; set; } = [];

    public Guid TenantId { get; set; }
}

public sealed class PriceList : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<PriceListEntry> Entries { get; set; } = [];

    public Guid TenantId { get; set; }
}

public sealed class License : AuditedEntity, IMultiTenant
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public List<string> Rights { get; set; } = [];

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public int PurchaseDays { get; set; } = 365;

    public string Status { get; set; } = LicenseStatuses.Draft;

    public Guid TenantId { get; set; }

    public bool IsValidOn(DateOnly day) => (ValidFrom is null || ValidFrom <= day) && (ValidUntil is null || day <= ValidUntil);
}

public sealed class LicenseAsset : Entity, IMultiTenant
{
    public Guid LicenseId { get; set; }

    public Guid AssetId { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class Buyout : AuditedEntity, IMultiTenant
{
    public Guid LicenseId { get; set; }

    public Guid RightsOwnerId { get; set; }

    public Guid PriceListId { get; set; }

    public required string Currency { get; set; }

    public decimal Basis { get; set; }

    public decimal FeePercentage { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidUntil { get; set; }

    public Guid TenantId { get; set; }

    public bool IsValidOn(DateOnly day) => (ValidFrom is null || ValidFrom <= day) && (ValidUntil is null || day <= ValidUntil);
}

public sealed class Purchase : AuditedEntity, IMultiTenant
{
    public required string OrderId { get; set; }

    public Guid LicenseId { get; set; }

    public required string LicenseName { get; set; }

    public string? ContextKey { get; set; }

    public Guid? GroupId { get; set; }

    /// <summary>The context or group name at the time of purchase.</summary>
    public string? TargetName { get; set; }

    public Guid BuyerId { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    public List<PriceDto> Prices { get; set; } = [];

    public List<Guid> AssetIds { get; set; } = [];

    public Guid TenantId { get; set; }
}

/// <summary>Purged assets leave the licenses they were in.</summary>
internal sealed class LicenseAssetPurgeListener(CoworkeeDbContext db) : Shareme.Graph.INodePurgeListener
{
    public Task OnPurgingAsync(IQueryable<Guid> nodeIds, CancellationToken cancellationToken) =>
        db.Set<LicenseAsset>().Where(a => nodeIds.Contains(a.AssetId)).ExecuteDeleteAsync(cancellationToken);
}

internal sealed class DrmModelContributor : IModelContributor
{
    private const string Schema = "sm";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingContact>(e =>
        {
            e.ToTable("BillingContacts", Schema);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Address).HasMaxLength(1000);
            e.Property(x => x.Iban).HasMaxLength(40);
            e.Property(x => x.Bic).HasMaxLength(20);
        });
        modelBuilder.Entity<RightsOwner>(e =>
        {
            e.ToTable("RightsOwners", Schema);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(1000);
        });
        modelBuilder.Entity<ContextGroup>(e =>
        {
            e.ToTable("ContextGroups", Schema);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ContextKeys).HasColumnType("jsonb");
        });
        modelBuilder.Entity<PriceList>(e =>
        {
            e.ToTable("PriceLists", Schema);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            Json(e.Property(x => x.Entries));
        });
        modelBuilder.Entity<License>(e =>
        {
            e.ToTable("Licenses", Schema);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Rights).HasColumnType("jsonb");
        });
        modelBuilder.Entity<LicenseAsset>(e =>
        {
            e.ToTable("LicenseAssets", Schema);
            e.HasIndex(x => new { x.LicenseId, x.AssetId }).IsUnique();
            e.HasIndex(x => x.AssetId);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Buyout>(e =>
        {
            e.ToTable("Buyouts", Schema);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Basis).HasPrecision(18, 2);
            e.Property(x => x.FeePercentage).HasPrecision(9, 4);
            e.HasIndex(x => x.LicenseId);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Purchase>(e =>
        {
            e.ToTable("Purchases", Schema);
            e.Property(x => x.OrderId).HasMaxLength(40);
            e.Property(x => x.LicenseName).HasMaxLength(200);
            e.Property(x => x.ContextKey).HasMaxLength(100);
            e.Property(x => x.TargetName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique();
            e.HasIndex(x => new { x.BuyerId, x.CreatedAt });
            Json(e.Property(x => x.Prices));
            e.Property(x => x.AssetIds).HasColumnType("jsonb");
        });
    }

    private static void Json<T>(PropertyBuilder<List<T>> property) =>
        property.HasColumnType("jsonb")
            .HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), s => JsonSerializer.Deserialize<List<T>>(s, (JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<T>>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
}

internal sealed class DrmPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(DrmPermissions.GroupName, "Licenses")
            .Add(DrmPermissions.Manage, "Manage licenses, rights owners, contacts, price lists and buyouts")
            .Add(DrmPermissions.Offers, "See license offers and prices")
            .Add(DrmPermissions.Purchase, "Buy licenses", DrmPermissions.Offers)
            .Add(DrmPermissions.Purchases, "See all purchases");
}

internal static class DrmMails
{
    public const string Customer = "Drm.PurchaseCustomer";
    public const string BillingContact = "Drm.PurchaseBillingContact";
    public const string Admin = "Drm.PurchaseAdmin";
}

internal sealed class DrmMailTemplates : IMailTemplateContributor
{
    private static readonly object Sample = new
    {
        purchase = new { order = "P-20261004-AB12CD", license = "Spring campaign", context = "DACH", starts = "2026-10-04", ends = "2027-10-04", prices = "1,200.00 EUR", assets = 12 },
        buyer = "Ada Lovelace",
        owner = "Studio North",
    };

    public void Define(MailTemplateContext context)
    {
        context.Add(new MailTemplateDefinition(DrmMails.Customer, "License purchase (buyer)", Sample, new Dictionary<string, MailTemplateContent>
        {
            ["en"] = new("Your license {{ purchase.license }} ({{ purchase.order }})",
                "<p>Thank you for your purchase.</p><p><strong>{{ purchase.license }}</strong> for {{ purchase.context }}, {{ purchase.starts }} to {{ purchase.ends }}, {{ purchase.assets }} files.</p><p>Price: {{ purchase.prices }}</p><p>Order {{ purchase.order }}</p>"),
            ["de"] = new("Ihre Lizenz {{ purchase.license }} ({{ purchase.order }})",
                "<p>Vielen Dank für Ihren Kauf.</p><p><strong>{{ purchase.license }}</strong> für {{ purchase.context }}, {{ purchase.starts }} bis {{ purchase.ends }}, {{ purchase.assets }} Dateien.</p><p>Preis: {{ purchase.prices }}</p><p>Bestellung {{ purchase.order }}</p>"),
        }));
        context.Add(new MailTemplateDefinition(DrmMails.BillingContact, "License purchase (billing contact)", Sample, new Dictionary<string, MailTemplateContent>
        {
            ["en"] = new("License sold: {{ purchase.license }} ({{ purchase.order }})",
                "<p>{{ buyer }} bought <strong>{{ purchase.license }}</strong> for {{ purchase.context }} ({{ purchase.starts }} to {{ purchase.ends }}), which includes rights of {{ owner }}.</p><p>Price: {{ purchase.prices }}, order {{ purchase.order }}.</p>"),
            ["de"] = new("Lizenz verkauft: {{ purchase.license }} ({{ purchase.order }})",
                "<p>{{ buyer }} hat <strong>{{ purchase.license }}</strong> für {{ purchase.context }} ({{ purchase.starts }} bis {{ purchase.ends }}) gekauft; sie enthält Rechte von {{ owner }}.</p><p>Preis: {{ purchase.prices }}, Bestellung {{ purchase.order }}.</p>"),
        }));
        context.Add(new MailTemplateDefinition(DrmMails.Admin, "License purchase (administration)", Sample, new Dictionary<string, MailTemplateContent>
        {
            ["en"] = new("New license purchase {{ purchase.order }}",
                "<p>{{ buyer }} bought <strong>{{ purchase.license }}</strong> for {{ purchase.context }} ({{ purchase.starts }} to {{ purchase.ends }}, {{ purchase.assets }} files).</p><p>Price: {{ purchase.prices }}</p>"),
            ["de"] = new("Neuer Lizenzkauf {{ purchase.order }}",
                "<p>{{ buyer }} hat <strong>{{ purchase.license }}</strong> für {{ purchase.context }} gekauft ({{ purchase.starts }} bis {{ purchase.ends }}, {{ purchase.assets }} Dateien).</p><p>Preis: {{ purchase.prices }}</p>"),
        }));
    }
}
