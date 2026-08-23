using FalloutLoc.Core.IO;
using FalloutLoc.Mo2.Models;

namespace FalloutLoc.Mo2;

public sealed class Mo2FileResolver(ISourceFileSystem sourceFileSystem)
{
    public IReadOnlyDictionary<string, PhysicalFileResolution> ResolvePluginMap(
        IReadOnlyList<string> activePlugins,
        string gameDataRoot,
        string overwriteRoot,
        Mo2ProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(activePlugins);
        var active = activePlugins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providers = active.ToDictionary(
            plugin => plugin,
            _ => new List<PhysicalFileProvider>(),
            StringComparer.OrdinalIgnoreCase);

        AddDirectoryProviders(gameDataRoot, PhysicalSourceKind.GameData, "Data", -1, null);
        foreach (var entry in profile.ModEntries.Where(entry =>
                     entry.Enabled && entry.DirectoryExists && !entry.IsSeparator))
        {
            AddDirectoryProviders(
                entry.Directory,
                PhysicalSourceKind.Mod,
                entry.Name,
                entry.EffectivePriority,
                entry.ProfileLine);
        }

        AddDirectoryProviders(overwriteRoot, PhysicalSourceKind.Overwrite, "overwrite", long.MaxValue, null);
        return providers.ToDictionary(
            item => item.Key,
            item => new PhysicalFileResolution
            {
                LogicalPath = item.Key,
                Providers = item.Value
                    .OrderByDescending(provider => provider.EffectivePriority)
                    .ThenBy(provider => provider.SourceName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            },
            StringComparer.OrdinalIgnoreCase);

        void AddDirectoryProviders(
            string directory,
            PhysicalSourceKind kind,
            string sourceName,
            long effectivePriority,
            int? profileLine)
        {
            foreach (var path in sourceFileSystem.EnumerateFiles(directory))
            {
                var fileName = Path.GetFileName(path);
                if (fileName is null || !active.Contains(fileName))
                {
                    continue;
                }

                providers[fileName].Add(Create(
                    fileName, path, kind, sourceName, effectivePriority, profileLine));
            }
        }
    }

    public PhysicalFileResolution Resolve(
        string logicalDataPath,
        string gameDataRoot,
        string overwriteRoot,
        Mo2ProfileSnapshot profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalDataPath);
        var logical = logicalDataPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (logical.Length == 0 || Path.IsPathRooted(logical)
            || logical.Split(Path.DirectorySeparatorChar).Any(component => component == ".."))
        {
            throw new ArgumentException("Logical Data path must be a relative path without parent traversal.", nameof(logicalDataPath));
        }

        var providers = new List<PhysicalFileProvider>();
        var gamePath = Path.Combine(gameDataRoot, logical);
        if (sourceFileSystem.FileExists(gamePath))
        {
            providers.Add(Create(logical, gamePath, PhysicalSourceKind.GameData, "Data", -1, null));
        }

        foreach (var entry in profile.ModEntries.Where(entry =>
                     entry.Enabled && entry.DirectoryExists && !entry.IsSeparator))
        {
            var candidate = Path.Combine(entry.Directory, logical);
            if (sourceFileSystem.FileExists(candidate))
            {
                providers.Add(Create(
                    logical,
                    candidate,
                    PhysicalSourceKind.Mod,
                    entry.Name,
                    entry.EffectivePriority,
                    entry.ProfileLine));
            }
        }

        var overwritePath = Path.Combine(overwriteRoot, logical);
        if (sourceFileSystem.FileExists(overwritePath))
        {
            providers.Add(Create(
                logical,
                overwritePath,
                PhysicalSourceKind.Overwrite,
                "overwrite",
                long.MaxValue,
                null));
        }

        return new PhysicalFileResolution
        {
            LogicalPath = logical,
            Providers = providers
                .OrderByDescending(provider => provider.EffectivePriority)
                .ThenBy(provider => provider.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public IReadOnlyDictionary<string, PhysicalFileResolution> ResolveDirectoryFiles(
        string logicalDataDirectory,
        string extension,
        string gameDataRoot,
        string overwriteRoot,
        Mo2ProfileSnapshot profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var logicalDirectory = NormalizeLogicalPath(logicalDataDirectory, nameof(logicalDataDirectory));
        var normalizedExtension = extension.StartsWith('.') ? extension : "." + extension;
        var providers = new Dictionary<string, List<PhysicalFileProvider>>(StringComparer.OrdinalIgnoreCase);

        AddRoot(gameDataRoot, PhysicalSourceKind.GameData, "Data", -1, null);
        foreach (var entry in profile.ModEntries.Where(entry =>
                     entry.Enabled && entry.DirectoryExists && !entry.IsSeparator))
        {
            AddRoot(entry.Directory, PhysicalSourceKind.Mod, entry.Name, entry.EffectivePriority, entry.ProfileLine);
        }

        AddRoot(overwriteRoot, PhysicalSourceKind.Overwrite, "overwrite", long.MaxValue, null);
        return providers.ToDictionary(
            item => item.Key,
            item => new PhysicalFileResolution
            {
                LogicalPath = item.Key,
                Providers = item.Value
                    .OrderByDescending(provider => provider.EffectivePriority)
                    .ThenBy(provider => provider.SourceName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            },
            StringComparer.OrdinalIgnoreCase);

        void AddRoot(
            string dataRoot,
            PhysicalSourceKind kind,
            string sourceName,
            long effectivePriority,
            int? profileLine)
        {
            var physicalDirectory = Path.Combine(dataRoot, logicalDirectory);
            if (!sourceFileSystem.DirectoryExists(physicalDirectory))
            {
                return;
            }

            var pending = new Stack<string>();
            pending.Push(physicalDirectory);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var child in sourceFileSystem.EnumerateDirectories(directory))
                {
                    pending.Push(child);
                }

                foreach (var file in sourceFileSystem.EnumerateFiles(directory)
                             .Where(file => Path.GetExtension(file).Equals(
                                 normalizedExtension,
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    var relative = Path.GetRelativePath(physicalDirectory, file);
                    var logicalPath = Path.Combine(logicalDirectory, relative);
                    if (!providers.TryGetValue(logicalPath, out var candidates))
                    {
                        candidates = [];
                        providers.Add(logicalPath, candidates);
                    }

                    candidates.Add(Create(
                        logicalPath, file, kind, sourceName, effectivePriority, profileLine));
                }
            }
        }
    }

    private static string NormalizeLogicalPath(string logicalDataPath, string parameterName)
    {
        var logical = logicalDataPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (logical.Length == 0 || Path.IsPathRooted(logical)
            || logical.Split(Path.DirectorySeparatorChar).Any(component => component == ".."))
        {
            throw new ArgumentException(
                "Logical Data path must be a relative path without parent traversal.",
                parameterName);
        }

        return logical;
    }

    private static PhysicalFileProvider Create(
        string logicalPath,
        string physicalPath,
        PhysicalSourceKind sourceKind,
        string sourceName,
        long effectivePriority,
        int? profileLine) => new()
        {
            LogicalPath = logicalPath,
            PhysicalPath = PathRules.NormalizeAbsolute(physicalPath),
            SourceKind = sourceKind,
            SourceName = sourceName,
            EffectivePriority = effectivePriority,
            ProfileLine = profileLine,
        };
}
