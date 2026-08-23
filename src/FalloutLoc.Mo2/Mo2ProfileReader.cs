using FalloutLoc.Core.IO;
using FalloutLoc.Mo2.Models;

namespace FalloutLoc.Mo2;

public sealed class Mo2ProfileReader(ISourceFileSystem sourceFileSystem)
{
    public Mo2ProfileSnapshot Read(string modsRoot, string profileDirectory)
    {
        var profileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(profileDirectory));
        var modlistPath = Path.Combine(profileDirectory, "modlist.txt");
        var pluginsPath = Path.Combine(profileDirectory, "plugins.txt");
        var loadOrderPath = Path.Combine(profileDirectory, "loadorder.txt");

        if (!sourceFileSystem.FileExists(modlistPath))
        {
            throw new FileNotFoundException("MO2 profile has no modlist.txt.", modlistPath);
        }

        if (!sourceFileSystem.FileExists(pluginsPath))
        {
            throw new FileNotFoundException("MO2 profile has no plugins.txt.", pluginsPath);
        }

        var provisionalEntries = ParseModlist(sourceFileSystem.ReadAllLines(modlistPath), modsRoot);
        var knownCount = provisionalEntries.Count(entry => entry.DirectoryExists);
        var entries = provisionalEntries
            .Select(entry => entry with
            {
                EffectivePriority = entry.DirectoryExists
                    ? knownCount - entry.Ordinal - 1
                    : int.MinValue,
            })
            .ToArray();
        var activePlugins = ParsePluginList(sourceFileSystem.ReadAllLines(pluginsPath));
        var hasLoadOrder = sourceFileSystem.FileExists(loadOrderPath);
        var loadOrder = hasLoadOrder
            ? ParsePluginList(sourceFileSystem.ReadAllLines(loadOrderPath))
            : activePlugins;

        return new Mo2ProfileSnapshot
        {
            Name = profileName,
            Directory = PathRules.NormalizeAbsolute(profileDirectory),
            ModlistPath = PathRules.NormalizeAbsolute(modlistPath),
            PluginsPath = PathRules.NormalizeAbsolute(pluginsPath),
            LoadOrderPath = hasLoadOrder ? PathRules.NormalizeAbsolute(loadOrderPath) : null,
            ModEntries = entries,
            ActivePlugins = activePlugins,
            LoadOrder = loadOrder,
            PluginAndLoadOrderMatch = activePlugins.SequenceEqual(loadOrder, StringComparer.OrdinalIgnoreCase),
        };
    }

    private IReadOnlyList<Mo2ModEntry> ParseModlist(IReadOnlyList<string> lines, string modsRoot)
    {
        var results = new List<Mo2ModEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownOrdinal = 0;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var prefix = line[0];
            var hasMarker = prefix is '+' or '-' or '*';
            var marker = hasMarker ? prefix : '+';
            var name = hasMarker
                ? line[1..].Trim()
                : line.Trim();
            if (name.Length == 0 || !names.Add(name))
            {
                continue;
            }

            var directory = Path.Combine(modsRoot, name);
            var directoryExists = sourceFileSystem.DirectoryExists(directory);
            results.Add(new Mo2ModEntry
            {
                Name = name,
                Marker = marker,
                Enabled = marker != '-',
                ProfileLine = lineIndex + 1,
                Ordinal = directoryExists ? knownOrdinal++ : -1,
                EffectivePriority = -1,
                Directory = PathRules.NormalizeAbsolute(directory),
                DirectoryExists = directoryExists,
                IsSeparator = name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase),
            });
        }

        return results;
    }

    private static IReadOnlyList<string> ParsePluginList(IReadOnlyList<string> lines)
    {
        var plugins = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('-'))
            {
                continue;
            }

            if (line[0] is '*' or '+')
            {
                line = line[1..].Trim();
            }

            if (line.Length > 0 && seen.Add(line))
            {
                plugins.Add(line);
            }
        }

        return plugins;
    }
}
