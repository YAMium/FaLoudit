using System.IO.Enumeration;

namespace FalloutLoc.Core.IO;

public readonly record struct SourceFileStamp(long Length, DateTime LastWriteTimeUtc);

/// <summary>One freshness check's directory entries. Never used to authorize a file open
/// or a write. Links and unobserved aliases use the normal guarded filesystem.</summary>
public sealed class IndexFileListing : ISourceFileListing
{
    private readonly SourceFileSystem _source;
    private readonly Dictionary<string, Listing> _directories = new(StringComparer.Ordinal);
    private sealed record Listing(string[] Directories, string[] Files, bool AllFiles);
    private readonly Dictionary<string, SourceFileStamp> _stamps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingDirectories = new(StringComparer.Ordinal);

    public IndexFileListing(SourceFileSystem source, IEnumerable<string> roots, IEnumerable<string> activePlugins)
    {
        _source = source;
        var plugins = activePlugins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Parallelize independent mod roots only. Merge on one thread; scheduling cannot
        // affect the resolver's original profile order, filename spelling or tie breaks.
        var uniqueRoots = roots.Select(PathRules.NormalizeAbsolute).Distinct(StringComparer.Ordinal).ToArray();
        var trees = new (Dictionary<string, Listing> Dirs, Dictionary<string, SourceFileStamp> Stamps)[uniqueRoots.Length];
        Parallel.ForAsync(0, trees.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) },
            (i, _) =>
            {
                trees[i] = ScanRoot(uniqueRoots[i]);
                return ValueTask.CompletedTask;
            }).GetAwaiter().GetResult(); // Propagate the original guarded I/O exception to the CLI.
        foreach (var tree in trees)
        {
            foreach (var pair in tree.Dirs) _directories.TryAdd(pair.Key, pair.Value);
            foreach (var pair in tree.Stamps) _stamps.TryAdd(pair.Key, pair.Value);
        }

        (Dictionary<string, Listing> Dirs, Dictionary<string, SourceFileStamp> Stamps) ScanRoot(string root)
        {
            var dirs = new Dictionary<string, Listing>(StringComparer.Ordinal);
            var stamps = new Dictionary<string, SourceFileStamp>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var path))
            {
                var directory = source.ReadDirectory(path);
                // Retain the original resolver's handling of junctions, including its
                // physical path spelling. Do not infer anything about their contents.
                if (!directory.FullName.Equals(path, StringComparison.Ordinal)) continue;
                var children = new List<string>();
                var files = new List<string>();
                bool allFiles = path == root; // The plugin map asks for every top-level name.
                var entries = new FileSystemEnumerable<FileSystemInfo>(path,
                    (ref FileSystemEntry entry) => entry.ToFileSystemInfo(),
                    new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false })
                {
                    ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                        allFiles || entry.IsDirectory || IsLooseExtension(Path.GetExtension(entry.FileName))
                };
                foreach (var entry in entries)
                {
                    var link = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (entry is DirectoryInfo)
                    {
                        children.Add(entry.FullName);
                        if (!link && !entry.Name.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase))
                            pending.Push(entry.FullName);
                    }
                    else if (entry is FileInfo file)
                    {
                        files.Add(file.FullName);
                        if (!link && (!allFiles || IsLooseExtension(file.Extension) || plugins.Contains(file.Name)))
                            stamps[file.FullName] = new(file.Length, file.LastWriteTimeUtc);
                    }
                }
                dirs.Add(path, new(children.ToArray(), files.ToArray(), allFiles));
            }
            return (dirs, stamps);
        }
    }

    private static bool IsLooseExtension(ReadOnlySpan<char> ext) =>
        ext.Equals(".ini", StringComparison.OrdinalIgnoreCase) || ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".gek", StringComparison.OrdinalIgnoreCase);

    public SourceFileStamp ReadStamp(string path) =>
        _stamps.TryGetValue(path, out var stamp) ? stamp : _source.ReadStamp(path);

    public IEnumerable<string> EnumerateFiles(string path) =>
        _directories.TryGetValue(path, out var dir) && dir.AllFiles ? dir.Files : _source.EnumerateFiles(path);

    public IEnumerable<string> EnumerateFiles(string path, string extension) =>
        (_directories.TryGetValue(path, out var dir) && (dir.AllFiles || IsLooseExtension(extension))
            ? dir.Files : _source.EnumerateFiles(path))
        .Where(file => Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> EnumerateDirectories(string path) =>
        _directories.TryGetValue(path, out var dir) ? dir.Directories : _source.EnumerateDirectories(path);

    public bool DirectoryExists(string path) =>
        _directories.ContainsKey(path) || (!AncestorMissing(path) && ProbeDirectory(path));

    public bool FileExists(string path) =>
        _stamps.ContainsKey(path) || (!AncestorMissing(path) && _source.FileExists(path));

    // Avoid thousands of repeated probes under the same missing NVSE directory.
    // Establish absence with the real guarded filesystem, not case-folded names.
    private bool AncestorMissing(string path)
    {
        for (var current = path; Path.GetDirectoryName(current) is { } parent; current = parent)
        {
            if (_missingDirectories.Contains(parent)) return true;
            if (_directories.ContainsKey(parent)) return current != path && !ProbeDirectory(current);
        }
        return false;
    }

    private bool ProbeDirectory(string path)
    {
        if (_missingDirectories.Contains(path)) return false;
        if (_source.DirectoryExists(path)) return true;
        _missingDirectories.Add(path);
        return false;
    }
}
