using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Backends.Models;

public sealed record PluginOpenRequest
{
    public required string Path { get; init; }

    public required GameMode Mode { get; init; }

    public int LoadOrderIndex { get; init; }

    public string? SourceMod { get; init; }
}
