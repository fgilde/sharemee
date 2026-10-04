using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shareme.Importer;

/// <summary>A problem found while mapping; names only the source table and primary key, never content.</summary>
public sealed record ImportProblem(string Code, string Table, long SourceId);

public sealed class ImportReport
{
    public SortedDictionary<string, int> Mapped { get; } = new(StringComparer.Ordinal);

    public SortedDictionary<string, int> Written { get; } = new(StringComparer.Ordinal);

    public List<ImportProblem> Problems { get; } = [];

    /// <summary>Rows deliberately left for later phases or without a counterpart, by reason; counts only.</summary>
    public SortedDictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);

    public void Skip(string reason) => Skipped[reason] = Skipped.GetValueOrDefault(reason) + 1;

    public void Problem(string code, string table, long sourceId) => Problems.Add(new ImportProblem(code, table, sourceId));

    public string Summary()
    {
        var text = new StringBuilder();
        foreach (var (name, count) in Mapped)
        {
            text.AppendLine($"{name,-24} mapped {count,8}   written {Written.GetValueOrDefault(name),8}");
        }

        foreach (var (reason, count) in Skipped)
        {
            text.AppendLine($"skipped {reason}: {count}");
        }

        foreach (var group in Problems.GroupBy(p => (p.Code, p.Table)).OrderBy(g => g.Key))
        {
            text.AppendLine($"problem {group.Key.Code} in {group.Key.Table}: {group.Count()}");
        }

        return text.ToString();
    }

    public Task SaveAsync(string path, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Mapped, Written, Skipped, Problems }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
}

/// <summary>
/// Target ids: a GUID identifier stays (links keep working), anything else gets a stable name based UUID within the target
/// tenant, so one source can go to several tenants. A kept GUID can live in one tenant only; the writer refuses a second.
/// </summary>
public static class SourceIds
{
    private static readonly Guid Namespace = new("6f1d6b0e-4f7e-5c31-9d2b-5a1e0c4d8b77");

    public static Guid For(Guid scope, string table, long sourceId, string? identifier) =>
        Guid.TryParse(identifier, out var id) && id != Guid.Empty ? id : Derived(scope, table, sourceId);

    /// <summary>UUID version 5 (RFC 9562) of "scope:table:id".</summary>
    public static Guid Derived(Guid scope, string table, long sourceId)
    {
        var name = Encoding.UTF8.GetBytes($"{scope:N}:{table}:{sourceId}");
        var bytes = Namespace.ToByteArray(bigEndian: true).Concat(name).ToArray();
        var hash = SHA1.HashData(bytes)[..16];
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash, bigEndian: true);
    }
}
