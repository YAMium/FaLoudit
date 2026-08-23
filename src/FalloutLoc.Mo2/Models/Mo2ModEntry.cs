namespace FalloutLoc.Mo2.Models;

public sealed record Mo2ModEntry
{
    public required string Name { get; init; }

    public required char Marker { get; init; }

    public required bool Enabled { get; init; }

    public required int ProfileLine { get; init; }

    public required int Ordinal { get; init; }

    public required int EffectivePriority { get; init; }

    public required string Directory { get; init; }

    public required bool DirectoryExists { get; init; }

    public required bool IsSeparator { get; init; }
}
