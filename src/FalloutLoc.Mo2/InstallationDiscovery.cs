using FalloutLoc.Core.Configuration;
using FalloutLoc.Core.IO;
using FalloutLoc.Mo2.Models;

namespace FalloutLoc.Mo2;

public sealed class InstallationDiscovery(ISourceFileSystem sourceFileSystem)
{
    public InstallationDiscoveryResult Discover(string inputRoot, string? profileOverride = null)
    {
        var root = PathRules.NormalizeAbsolute(inputRoot);
        if (!sourceFileSystem.DirectoryExists(root))
        {
            throw new DirectoryNotFoundException($"Build root does not exist: {root}");
        }

        var executable = Path.Combine(root, "ModOrganizer.exe");
        var iniPath = Path.Combine(root, "ModOrganizer.ini");
        if (!sourceFileSystem.FileExists(executable) || !sourceFileSystem.FileExists(iniPath))
        {
            throw new InvalidDataException($"Portable Mod Organizer installation was not found at: {root}");
        }

        var ini = ParseIni(sourceFileSystem.ReadAllLines(iniPath));
        var baseRoot = ini.TryGetValue("base_directory", out var configuredBase)
            && !string.IsNullOrWhiteSpace(configuredBase)
            ? ResolvePath(root, root, configuredBase)
            : root;
        var modsRoot = ResolveConfiguredDirectory(root, baseRoot, ini, "mod_directory", "mods");
        var profilesRoot = ResolveConfiguredDirectory(root, baseRoot, ini, "profiles_directory", "profiles");
        var overwriteRoot = ResolveConfiguredDirectory(root, baseRoot, ini, "overwrite_directory", "overwrite");
        var gameRoot = ResolvePath(root, root, RequireSetting(ini, "gamePath"));
        var dataRoot = Path.Combine(gameRoot, "Data");

        RequireDirectory(modsRoot, "MO2 mods");
        RequireDirectory(profilesRoot, "MO2 profiles");
        RequireDirectory(overwriteRoot, "MO2 overwrite");
        RequireDirectory(gameRoot, "runtime game");
        RequireDirectory(dataRoot, "runtime Data");

        var profiles = sourceFileSystem.EnumerateDirectories(profilesRoot)
            .Where(path => sourceFileSystem.FileExists(Path.Combine(path, "modlist.txt")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedProfile = profileOverride ?? DecodeIniValue(RequireSetting(ini, "selected_profile"));
        if (!profiles.Contains(selectedProfile, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Selected profile '{selectedProfile}' was not found. Available: {string.Join(", ", profiles)}");
        }

        selectedProfile = profiles.Single(name => name.Equals(selectedProfile, StringComparison.OrdinalIgnoreCase));
        var profileDirectory = Path.Combine(profilesRoot, selectedProfile);
        var profile = new Mo2ProfileReader(sourceFileSystem).Read(modsRoot, profileDirectory);
        var mode = DetectMode(profile.ActivePlugins, ini);
        var warnings = new List<string>();
        if (!profile.PluginAndLoadOrderMatch)
        {
            warnings.Add("plugins.txt and loadorder.txt differ; load-order authority requires explicit review.");
        }

        var evidence = BuildEvidence(mode, profile);
        var availableActualMods = sourceFileSystem.EnumerateDirectories(modsRoot)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Count(name => !name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase));

        return new InstallationDiscoveryResult
        {
            InputRoot = root,
            Mo2Root = root,
            ModOrganizerExecutable = PathRules.NormalizeAbsolute(executable),
            ModOrganizerIni = PathRules.NormalizeAbsolute(iniPath),
            Mo2Version = ini.GetValueOrDefault("version") is { } version ? DecodeIniValue(version) : "unknown",
            ModsRoot = PathRules.NormalizeAbsolute(modsRoot),
            ProfilesRoot = PathRules.NormalizeAbsolute(profilesRoot),
            OverwriteRoot = PathRules.NormalizeAbsolute(overwriteRoot),
            GameRoot = PathRules.NormalizeAbsolute(gameRoot),
            DataRoot = PathRules.NormalizeAbsolute(dataRoot),
            SelectedProfile = selectedProfile,
            AvailableProfiles = profiles,
            AvailableActualMods = availableActualMods,
            Profile = profile,
            Mode = mode,
            Evidence = evidence,
            Warnings = warnings,
        };
    }

    private static Dictionary<string, string> ParseIni(IReadOnlyList<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }

        return values;
    }

    private static string RequireSetting(IReadOnlyDictionary<string, string> ini, string key) =>
        ini.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"ModOrganizer.ini has no required setting '{key}'.");

    private static string ResolveConfiguredDirectory(
        string iniRoot,
        string baseRoot,
        IReadOnlyDictionary<string, string> ini,
        string key,
        string fallback) =>
        ini.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? ResolvePath(iniRoot, baseRoot, value)
            : Path.Combine(baseRoot, fallback);

    private static string ResolvePath(string iniRoot, string baseRoot, string value)
    {
        var decoded = DecodeIniValue(value)
            .Replace("%BASE_DIR%", baseRoot, StringComparison.OrdinalIgnoreCase)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(decoded)
            ? PathRules.NormalizeAbsolute(decoded)
            : PathRules.NormalizeAbsolute(Path.Combine(iniRoot, decoded));
    }

    private static string DecodeIniValue(string value)
    {
        const string prefix = "@ByteArray(";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(')'))
        {
            value = value[prefix.Length..^1];
        }

        return value.Replace(@"\\", @"\", StringComparison.Ordinal);
    }

    private void RequireDirectory(string path, string description)
    {
        if (!sourceFileSystem.DirectoryExists(path))
        {
            throw new DirectoryNotFoundException($"Required {description} directory does not exist: {path}");
        }
    }

    private static GameMode DetectMode(
        IReadOnlyList<string> activePlugins,
        IReadOnlyDictionary<string, string> ini)
    {
        if (activePlugins.Contains("TaleOfTwoWastelands.esm", StringComparer.OrdinalIgnoreCase)
            && activePlugins.Contains("FalloutNV.esm", StringComparer.OrdinalIgnoreCase)
            && activePlugins.Contains("Fallout3.esm", StringComparer.OrdinalIgnoreCase))
        {
            return GameMode.TaleOfTwoWastelands;
        }

        if (activePlugins.Contains("FalloutNV.esm", StringComparer.OrdinalIgnoreCase)
            || ini.GetValueOrDefault("gameName")?.Contains("New Vegas", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GameMode.FalloutNewVegas;
        }

        if (activePlugins.Contains("Fallout3.esm", StringComparer.OrdinalIgnoreCase))
        {
            return GameMode.Fallout3;
        }

        throw new InvalidDataException("Unable to detect Fallout 3, Fallout New Vegas, or TTW mode.");
    }

    private static IReadOnlyList<string> BuildEvidence(GameMode mode, Mo2ProfileSnapshot profile)
    {
        var evidence = new List<string>
        {
            $"Active plugin count: {profile.ActivePlugins.Count}",
            $"Enabled actual mod count: {profile.EnabledActualMods}",
            $"Enabled separator count: {profile.EnabledSeparators}",
        };
        if (mode == GameMode.TaleOfTwoWastelands)
        {
            evidence.Add("FalloutNV.esm, Fallout3.esm, and TaleOfTwoWastelands.esm are active in one FNV load order.");
        }

        return evidence;
    }
}
