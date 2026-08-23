using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Mo2.Models;

public sealed record InstallationDiscoveryResult
{
    public required string InputRoot { get; init; }

    public required string Mo2Root { get; init; }

    public required string ModOrganizerExecutable { get; init; }

    public required string ModOrganizerIni { get; init; }

    public required string Mo2Version { get; init; }

    public required string ModsRoot { get; init; }

    public required string ProfilesRoot { get; init; }

    public required string OverwriteRoot { get; init; }

    public required string GameRoot { get; init; }

    public required string DataRoot { get; init; }

    public required string SelectedProfile { get; init; }

    public required IReadOnlyList<string> AvailableProfiles { get; init; }

    public required int AvailableActualMods { get; init; }

    public required Mo2ProfileSnapshot Profile { get; init; }

    public required GameMode Mode { get; init; }

    public required IReadOnlyList<string> Evidence { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
