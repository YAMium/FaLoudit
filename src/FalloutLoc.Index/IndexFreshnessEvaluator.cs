using FalloutLoc.Index.Models;

namespace FalloutLoc.Index;

public enum IndexFreshnessKind
{
    Fresh,
    Stale,
    Missing,
    Unreadable,
    Incompatible,
}

public sealed record IndexFreshnessResult
{
    public required IndexFreshnessKind Kind { get; init; }
    public required string DatabasePath { get; init; }
    public required string CurrentFingerprint { get; init; }
    public string? IndexedFingerprint { get; init; }
    public IndexSnapshotStatus? Snapshot { get; init; }
    public required string Explanation { get; init; }
    public bool IsFresh => Kind == IndexFreshnessKind.Fresh;
}

public static class IndexFreshnessEvaluator
{
    public static IndexFreshnessResult Evaluate(
        string databasePath,
        string currentFingerprint,
        string? expectedBackendName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFingerprint);
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
        {
            return Result(IndexFreshnessKind.Missing, "No published index exists.");
        }

        try
        {
            var snapshot = new SqliteIndexRepository(path).GetStatus();
            if (expectedBackendName is not null
                && !snapshot.BackendName.Equals(expectedBackendName, StringComparison.Ordinal))
            {
                return new IndexFreshnessResult
                {
                    Kind = IndexFreshnessKind.Incompatible,
                    DatabasePath = path,
                    CurrentFingerprint = currentFingerprint,
                    IndexedFingerprint = snapshot.LoadOrderFingerprint,
                    Snapshot = snapshot,
                    Explanation = $"The published index uses incompatible backend/cache identity '{snapshot.BackendName}'; expected '{expectedBackendName}'.",
                };
            }

            var fresh = snapshot.LoadOrderFingerprint.Equals(currentFingerprint, StringComparison.Ordinal);
            return new IndexFreshnessResult
            {
                Kind = fresh ? IndexFreshnessKind.Fresh : IndexFreshnessKind.Stale,
                DatabasePath = path,
                CurrentFingerprint = currentFingerprint,
                IndexedFingerprint = snapshot.LoadOrderFingerprint,
                Snapshot = snapshot,
                Explanation = fresh
                    ? "The configured profile, active load order, physical providers, and plugin file metadata match the published snapshot."
                    : "The configured profile, active load order, physical providers, or plugin file metadata changed after the published snapshot.",
            };
        }
        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
        {
            return Result(IndexFreshnessKind.Unreadable,
                $"The published index cannot be validated: {exception.GetType().Name}: {exception.Message}");
        }

        IndexFreshnessResult Result(IndexFreshnessKind kind, string explanation) => new()
        {
            Kind = kind,
            DatabasePath = path,
            CurrentFingerprint = currentFingerprint,
            Explanation = explanation,
        };
    }
}
