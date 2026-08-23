namespace FalloutLoc.Mo2.Models;

public sealed record Mo2ProfileSnapshot
{
    public required string Name { get; init; }

    public required string Directory { get; init; }

    public required string ModlistPath { get; init; }

    public required string PluginsPath { get; init; }

    public string? LoadOrderPath { get; init; }

    public required IReadOnlyList<Mo2ModEntry> ModEntries { get; init; }

    public required IReadOnlyList<string> ActivePlugins { get; init; }

    public required IReadOnlyList<string> LoadOrder { get; init; }

    public required bool PluginAndLoadOrderMatch { get; init; }

    public int EnabledActualMods => ModEntries.Count(entry =>
        entry.Enabled && entry.DirectoryExists && !entry.IsSeparator);

    public int EnabledSeparators => ModEntries.Count(entry =>
        entry.Enabled && entry.DirectoryExists && entry.IsSeparator);

    public int EnabledEntriesIncludingSeparators => ModEntries.Count(entry =>
        entry.Enabled && entry.DirectoryExists);
}
