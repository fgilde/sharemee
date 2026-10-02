namespace Shareme.Contracts.Graph;

/// <summary>What a restriction takes away from users with its role.</summary>
public static class RestrictionKinds
{
    /// <summary>The property is neither shown nor changeable.</summary>
    public const string HiddenProperty = "hiddenProperty";

    /// <summary>The property is shown but cannot be changed.</summary>
    public const string ReadOnlyProperty = "readOnlyProperty";

    /// <summary>Nodes having the list value (<c>Value</c>) in the property are out of reach: not listed, not found, not opened.</summary>
    public const string HiddenListValue = "hiddenListValue";

    /// <summary>The rendition kind (<c>Value</c>) cannot be fetched.</summary>
    public const string BlockedRendition = "blockedRendition";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { HiddenProperty, ReadOnlyProperty, HiddenListValue, BlockedRendition };
}

public sealed record RestrictionDto(Guid Id, Guid RoleId, string Kind, string? PropertyKey, string? Value);

public sealed record RestrictionRequest(Guid RoleId, string Kind, string? PropertyKey = null, string? Value = null);
