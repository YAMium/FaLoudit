using System.Text.RegularExpressions;

namespace FalloutLoc.Backends.Engine;

public sealed partial class StewieGameSettingIniParser
{
    public IReadOnlyList<StewieGameSettingIniEntry> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var inGameSettings = false;
        var entries = new Dictionary<string, StewieGameSettingIniEntry>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var sourceLine in lines)
        {
            lineNumber++;
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inGameSettings = line[1..^1].Trim()
                    .Equals("GameSettings", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inGameSettings)
            {
                continue;
            }

            var separator = sourceLine.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var editorId = sourceLine[..separator].Trim();
            if (!StringGameSettingNameRegex().IsMatch(editorId))
            {
                continue;
            }

            var value = sourceLine[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            }

            entries[editorId] = new StewieGameSettingIniEntry(editorId, value, lineNumber);
        }

        return entries.Values
            .OrderBy(entry => entry.LineNumber)
            .ToArray();
    }

    [GeneratedRegex("^[sS][A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex StringGameSettingNameRegex();
}

public sealed record StewieGameSettingIniEntry(string EditorId, string Value, int LineNumber);
