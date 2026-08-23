namespace FalloutLoc.Core.IO;

public sealed class ReadOnlySourceGuard
{
    private static readonly HashSet<string> AllowedWorkspaceDirectories = new(
        ["config", "cache", "index", "logs", "reports", "samples", "fixtures"],
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string[] _sourceRoots;

    public ReadOnlySourceGuard(IEnumerable<string> sourceRoots, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(sourceRoots);
        WorkspaceRoot = PathRules.ResolvePhysicalPath(workspaceRoot);
        _sourceRoots = sourceRoots
            .Select(PathRules.ResolvePhysicalPath)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();

        foreach (var sourceRoot in _sourceRoots)
        {
            if (PathRules.IsSameOrDescendant(WorkspaceRoot, sourceRoot)
                || PathRules.IsSameOrDescendant(sourceRoot, WorkspaceRoot))
            {
                throw new SafetyViolationException(
                    $"Workspace and source roots must not overlap. Workspace: {WorkspaceRoot}; source: {sourceRoot}");
            }
        }
    }

    public string WorkspaceRoot { get; }

    public IReadOnlyList<string> SourceRoots => _sourceRoots;

    public string EnsureReadableSource(string path)
    {
        var resolved = PathRules.ResolvePhysicalPath(path);
        if (!_sourceRoots.Any(root => PathRules.IsSameOrDescendant(resolved, root)))
        {
            throw new SafetyViolationException($"Path is outside configured read-only source roots: {path}");
        }

        return resolved;
    }

    public string EnsureWritableDestination(string path)
    {
        var normalized = PathRules.NormalizeAbsolute(path);
        var resolved = PathRules.ResolvePhysicalPath(normalized);

        if (!PathRules.IsSameOrDescendant(normalized, WorkspaceRoot)
            || !PathRules.IsSameOrDescendant(resolved, WorkspaceRoot))
        {
            throw new SafetyViolationException($"Write destination is outside the workspace: {path}");
        }

        foreach (var sourceRoot in _sourceRoots)
        {
            if (PathRules.IsSameOrDescendant(normalized, sourceRoot)
                || PathRules.IsSameOrDescendant(resolved, sourceRoot))
            {
                throw new SafetyViolationException($"Write destination is inside a read-only source root: {path}");
            }
        }

        var relative = Path.GetRelativePath(WorkspaceRoot, normalized);
        if (relative.Equals(".", StringComparison.Ordinal))
        {
            throw new SafetyViolationException("The workspace root itself is not a file destination.");
        }

        var firstComponent = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries)[0];
        if (!AllowedWorkspaceDirectories.Contains(firstComponent))
        {
            throw new SafetyViolationException(
                $"Writes are allowed only under: {string.Join(", ", AllowedWorkspaceDirectories.Order())}. Destination: {path}");
        }

        return normalized;
    }
}
