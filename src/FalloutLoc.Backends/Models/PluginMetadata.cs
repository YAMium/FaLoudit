using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Backends.Models;

public sealed record PluginMetadata
{
    public required string PluginName { get; init; }

    public required string PhysicalPath { get; init; }

    public required GameMode Mode { get; init; }

    public required int LoadOrderIndex { get; init; }

    public string? SourceMod { get; init; }

    public required IReadOnlyList<string> Masters { get; init; }
}
