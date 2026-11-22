namespace FalloutLoc.Core.IO;

public static class PathRules
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string NormalizeAbsolute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static bool IsSameOrDescendant(string candidate, string root)
    {
        var normalizedCandidate = NormalizeAbsolute(candidate);
        var normalizedRoot = NormalizeAbsolute(root);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedCandidate);

        if (relative.Equals(".", StringComparison.Ordinal))
        {
            return true;
        }

        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal))
        {
            return false;
        }

        return !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    public static bool PathEquals(string left, string right) =>
        NormalizeAbsolute(left).Equals(NormalizeAbsolute(right), PathComparison);

    public static string ResolvePhysicalPath(string path)
    {
        var fullPath = NormalizeAbsolute(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException($"Path has no root: {path}", nameof(path));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative.Equals(".", StringComparison.Ordinal))
        {
            return NormalizeAbsolute(root);
        }

        var current = root;
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, component);
            // FileSystemInfo.Attributes also works for directories and returns -1
            // for missing entries. One attribute lookup per component, including
            // ancestors; never cache a link decision between filesystem operations.
            var info = new FileInfo(next);
            var attributes = info.Attributes;
            if (attributes != (FileAttributes)(-1) && (attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo link = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(next) : info;
                var target = link.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new SafetyViolationException($"Cannot resolve reparse point: {next}");
                current = target.FullName;
            }
            else
            {
                current = next;
            }
        }

        return NormalizeAbsolute(current);
    }
}
