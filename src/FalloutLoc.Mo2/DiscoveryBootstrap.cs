using FalloutLoc.Core.IO;

namespace FalloutLoc.Mo2;

public static class DiscoveryBootstrap
{
    public static IReadOnlyList<string> FindSourceRoots(string inputRoot)
    {
        var root = PathRules.NormalizeAbsolute(inputRoot);
        var roots = new List<string> { root };
        var iniPath = Path.Combine(root, "ModOrganizer.ini");
        if (!File.Exists(iniPath))
        {
            return roots;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(iniPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';') || line.StartsWith('['))
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals > 0)
            {
                values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }

        var baseRoot = values.TryGetValue("base_directory", out var baseValue)
            ? Resolve(root, root, baseValue)
            : root;
        AddIfExternal(roots, root, baseRoot);

        foreach (var key in new[] { "gamePath", "mod_directory", "profiles_directory", "overwrite_directory" })
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var resolved = key.Equals("gamePath", StringComparison.OrdinalIgnoreCase)
                ? Resolve(root, root, value)
                : Resolve(root, baseRoot, value);
            AddIfExternal(roots, root, resolved);
        }

        return roots
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
    }

    private static string Resolve(string iniRoot, string baseRoot, string value)
    {
        const string prefix = "@ByteArray(";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(')'))
        {
            value = value[prefix.Length..^1];
        }

        value = value.Replace(@"\\", @"\", StringComparison.Ordinal)
            .Replace("%BASE_DIR%", baseRoot, StringComparison.OrdinalIgnoreCase)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(value)
            ? PathRules.NormalizeAbsolute(value)
            : PathRules.NormalizeAbsolute(Path.Combine(iniRoot, value));
    }

    private static void AddIfExternal(ICollection<string> roots, string inputRoot, string candidate)
    {
        if (!PathRules.IsSameOrDescendant(candidate, inputRoot))
        {
            roots.Add(candidate);
        }
    }
}
