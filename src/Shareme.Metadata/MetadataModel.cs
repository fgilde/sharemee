using System.Text.Json;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shareme.Contracts.Metadata;

namespace Shareme.Metadata;

public sealed class PropertyGroup : AuditedEntity, IMultiTenant
{
    public required string Key { get; set; }

    public required string Name { get; set; }

    public int SortIndex { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class PropertyDefinition : AuditedEntity, IMultiTenant
{
    public required string Key { get; set; }

    public Guid? GroupId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public string ValueType { get; set; } = PropertyValueTypes.String;

    public string ListMode { get; set; } = ListModes.None;

    public bool AllowCustomValues { get; set; }

    public bool MultiValue { get; set; }

    public bool Localizable { get; set; }

    public bool HasContext { get; set; }

    public bool Editable { get; set; } = true;

    public bool IsSystem { get; set; }

    public string Searchable { get; set; } = SearchModes.None;

    public bool Facet { get; set; }

    public bool IgnoreOnNewVersion { get; set; }

    public bool InheritToChildren { get; set; }

    public List<string> NodeTypeKeys { get; set; } = [];

    public PropertyValidation Validation { get; set; } = new();

    public int SortIndex { get; set; }

    public Guid TenantId { get; set; }

    public List<PropertyListValue> ListValues { get; set; } = [];

    public bool AppliesTo(string nodeTypeKey) => NodeTypeKeys.Count == 0 || NodeTypeKeys.Contains(nodeTypeKey);
}

public sealed class PropertyListValue : Entity
{
    public Guid DefinitionId { get; set; }

    public required string Key { get; set; }

    public required string Name { get; set; }

    public int SortIndex { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class PropertySet : AuditedEntity, IMultiTenant
{
    public required string NodeTypeKey { get; set; }

    public required string Name { get; set; }

    public bool IsDefault { get; set; }

    public List<PropertySetItem> Items { get; set; } = [];

    public List<PropertySetCondition> Conditions { get; set; } = [];

    public Guid TenantId { get; set; }
}

public sealed class DataLanguage : Entity, IMultiTenant
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsDefault { get; set; }

    public Guid TenantId { get; set; }
}

public sealed class UserContext : Entity, IMultiTenant
{
    public required string Key { get; set; }

    public required string Name { get; set; }

    public bool IsDefault { get; set; }

    public Guid TenantId { get; set; }
}

internal sealed class MetadataModelContributor : IModelContributor
{
    private const string Schema = "sm";

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PropertyGroup>(group =>
        {
            group.ToTable("PropertyGroups", Schema);
            group.Property(g => g.Key).HasMaxLength(100);
            group.Property(g => g.Name).HasMaxLength(200);
            group.HasIndex(g => new { g.TenantId, g.Key }).IsUnique();
        });

        modelBuilder.Entity<PropertyDefinition>(definition =>
        {
            definition.ToTable("PropertyDefinitions", Schema);
            definition.Property(d => d.Key).HasMaxLength(100);
            definition.Property(d => d.Name).HasMaxLength(200);
            definition.Property(d => d.Description).HasMaxLength(2000);
            definition.Property(d => d.ValueType).HasMaxLength(20);
            definition.Property(d => d.ListMode).HasMaxLength(20);
            definition.Property(d => d.Searchable).HasMaxLength(20);
            definition.Property(d => d.NodeTypeKeys).HasColumnType("jsonb");
            Json(definition.Property(d => d.Validation));
            definition.HasIndex(d => new { d.TenantId, d.Key }).IsUnique();
            definition.HasOne<PropertyGroup>().WithMany().HasForeignKey(d => d.GroupId).OnDelete(DeleteBehavior.SetNull);
            definition.HasMany(d => d.ListValues).WithOne().HasForeignKey(v => v.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PropertyListValue>(value =>
        {
            value.ToTable("PropertyListValues", Schema);
            value.Property(v => v.Key).HasMaxLength(100);
            value.Property(v => v.Name).HasMaxLength(200);
            value.HasIndex(v => new { v.DefinitionId, v.Key }).IsUnique();
        });

        modelBuilder.Entity<PropertySet>(set =>
        {
            set.ToTable("PropertySets", Schema);
            set.Property(s => s.NodeTypeKey).HasMaxLength(100);
            set.Property(s => s.Name).HasMaxLength(200);
            Json(set.Property(s => s.Items));
            Json(set.Property(s => s.Conditions));
            set.HasIndex(s => new { s.TenantId, s.NodeTypeKey });
        });

        modelBuilder.Entity<DataLanguage>(language =>
        {
            language.ToTable("DataLanguages", Schema);
            language.Property(l => l.Code).HasMaxLength(20);
            language.Property(l => l.Name).HasMaxLength(100);
            language.HasIndex(l => new { l.TenantId, l.Code }).IsUnique();
        });

        modelBuilder.Entity<UserContext>(context =>
        {
            context.ToTable("UserContexts", Schema);
            context.Property(c => c.Key).HasMaxLength(100);
            context.Property(c => c.Name).HasMaxLength(200);
            context.HasIndex(c => new { c.TenantId, c.Key }).IsUnique();
        });
    }

    private static void Json<T>(PropertyBuilder<T> property)
        where T : class =>
        property.HasColumnType("jsonb")
            .HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), s => JsonSerializer.Deserialize<T>(s, (JsonSerializerOptions?)null)!)
            .Metadata.SetValueComparer(new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
}
