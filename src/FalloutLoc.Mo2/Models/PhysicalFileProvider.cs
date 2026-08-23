namespace FalloutLoc.Mo2.Models;

public enum PhysicalSourceKind
{
    GameData,
    Mod,
    Overwrite,
}

public sealed record PhysicalFileProvider
{
    public required string LogicalPath { get; init; }

    public required string PhysicalPath { get; init; }

    public required PhysicalSourceKind SourceKind { get; init; }

    public required string SourceName { get; init; }

    public required long EffectivePriority { get; init; }

    public int? ProfileLine { get; init; }
}

public sealed record PhysicalFileResolution
{
    public required string LogicalPath { get; init; }

    public required IReadOnlyList<PhysicalFileProvider> Providers { get; init; }

    public PhysicalFileProvider? Winner => Providers.Count == 0 ? null : Providers[0];
}
