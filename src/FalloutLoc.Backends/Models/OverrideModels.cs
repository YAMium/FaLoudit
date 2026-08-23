using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Backends.Models;

public sealed record ActivePluginSource
{
    public required int LoadOrderIndex { get; init; }

    public required string PluginName { get; init; }

    public required string PhysicalPath { get; init; }

    public string? SourceMod { get; init; }
}

public sealed record OverrideTraceRequest
{
    public required GameMode Mode { get; init; }

    public required string FormKey { get; init; }

    public required IReadOnlyList<ActivePluginSource> ActivePlugins { get; init; }
}

public sealed record OverrideOccurrence
{
    public required ActivePluginSource Plugin { get; init; }

    public required RecordOccurrence Record { get; init; }
}

public sealed record OverrideTraceResult
{
    public required string FormKey { get; init; }

    public required IReadOnlyList<OverrideOccurrence> Chain { get; init; }

    public OverrideOccurrence? Winner => Chain.Count == 0 ? null : Chain[^1];
}
