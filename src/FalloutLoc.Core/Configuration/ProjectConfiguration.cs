namespace FalloutLoc.Core.Configuration;

public sealed record ProjectConfiguration
{
    public int SchemaVersion { get; init; } = 1;

    public required GameMode Mode { get; init; }

    public required string Mo2Root { get; init; }

    public required string ModsRoot { get; init; }

    public required string ProfilesRoot { get; init; }

    public required string ProfileName { get; init; }

    public required string OverwriteRoot { get; init; }

    public required string GameRoot { get; init; }

    public required string DataRoot { get; init; }
}
