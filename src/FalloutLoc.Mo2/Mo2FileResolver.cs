using FalloutLoc.Core.IO;
using FalloutLoc.Mo2.Models;

namespace FalloutLoc.Mo2;

public sealed class Mo2FileResolver(ISourceFileListing sourceFileSystem)
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

        var providers = SourceReadBatch.Map(ProviderRoots(gameDataRoot, overwriteRoot, profile), sourceFileSystem, root =>
        {
            var path = Path.Combine(root.Directory, logical);
            return sourceFileSystem.FileExists(path) ? Create(logical, path, root.Kind, root.Name, root.Priority, root.Line) : null;
        });

        return new PhysicalFileResolution
        {
            LogicalPath = logical,
            Providers = providers.OfType<PhysicalFileProvider>()
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
        var logicalDirectory = logicalDataDirectory.Trim() == "."
            ? string.Empty
            : NormalizeLogicalPath(logicalDataDirectory, nameof(logicalDataDirectory));
        var normalizedExtension = extension.StartsWith('.') ? extension : "." + extension;
        var providers = new Dictionary<string, List<PhysicalFileProvider>>(StringComparer.OrdinalIgnoreCase);

        var batches = SourceReadBatch.Map(ProviderRoots(gameDataRoot, overwriteRoot, profile), sourceFileSystem, ReadRoot);
        foreach (var file in batches.SelectMany(batch => batch))
        {
            if (!providers.TryGetValue(file.LogicalPath, out var candidates))
                providers.Add(file.LogicalPath, candidates = []);
            candidates.Add(file);
        }
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

        IReadOnlyList<PhysicalFileProvider> ReadRoot(ProviderRoot root)
        {
            var physicalDirectory = Path.Combine(root.Directory, logicalDirectory);
            if (!sourceFileSystem.DirectoryExists(physicalDirectory))
            {
                return [];
            }

            var found = new List<PhysicalFileProvider>();
            var pending = new Stack<string>();
            pending.Push(physicalDirectory);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var child in sourceFileSystem.EnumerateDirectories(directory)
                             .Where(child => !Path.GetFileName(child).EndsWith(
                                 ".mohidden", StringComparison.OrdinalIgnoreCase)))
                {
                    pending.Push(child);
                }

                foreach (var file in sourceFileSystem.EnumerateFiles(directory, normalizedExtension))
                {
                    var relative = Path.GetRelativePath(physicalDirectory, file);
                    var logicalPath = logicalDirectory.Length == 0
                        ? relative
                        : Path.Combine(logicalDirectory, relative);
                    if ((logicalDirectory.Length == 0
                            && logicalPath.Equals("meta.ini", StringComparison.OrdinalIgnoreCase))
                        || Path.GetFileName(logicalPath).Equals(
                            "desktop.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        // MO2 metadata and Windows folder metadata are not game configuration.
                        continue;
                    }

                    found.Add(Create(logicalPath, file, root.Kind, root.Name, root.Priority, root.Line));
                }
            }
            return found;
        }
    }

    private readonly record struct ProviderRoot(string Directory, PhysicalSourceKind Kind, string Name, long Priority, int? Line);

    private static List<ProviderRoot> ProviderRoots(string data, string overwrite, Mo2ProfileSnapshot profile)
    {
        var roots = new List<ProviderRoot> { new(data, PhysicalSourceKind.GameData, "Data", -1, null) };
        foreach (var entry in profile.ModEntries.Where(entry => entry.Enabled && entry.DirectoryExists && !entry.IsSeparator))
            roots.Add(new(entry.Directory, PhysicalSourceKind.Mod, entry.Name, entry.EffectivePriority, entry.ProfileLine));
        roots.Add(new(overwrite, PhysicalSourceKind.Overwrite, "overwrite", long.MaxValue, null));
        return roots;
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
