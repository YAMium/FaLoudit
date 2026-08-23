using FalloutLoc.Core.IO;

namespace FalloutLoc.Core.Tests;

public sealed class ReadOnlySourceGuardTests
{
    [Fact]
    public void RejectsWritesInsideSourceAndOutsideAllowedWorkspaceDirectories()
    {
        using var area = new TestArea();
        var guard = new ReadOnlySourceGuard([area.Source], area.Workspace);

        Assert.Throws<SafetyViolationException>(() =>
            guard.EnsureWritableDestination(Path.Combine(area.Source, "plugin.esp")));
        Assert.Throws<SafetyViolationException>(() =>
            guard.EnsureWritableDestination(Path.Combine(area.Workspace, "other", "file.txt")));
        Assert.Throws<SafetyViolationException>(() =>
            guard.EnsureWritableDestination(Path.Combine(area.Workspace, "config", "..", "other", "file.txt")));
    }

    [Fact]
    public void AllowsOnlyWhitelistedWorkspaceDestination()
    {
        using var area = new TestArea();
        var guard = new ReadOnlySourceGuard([area.Source], area.Workspace);
        var expected = Path.GetFullPath(Path.Combine(area.Workspace, "config", "project.json"));

        var actual = guard.EnsureWritableDestination(expected);

        Assert.Equal(expected, actual, ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void AtomicWorkspaceWriteDoesNotTouchSource()
    {
        using var area = new TestArea();
        var sourceFile = Path.Combine(area.Source, "plugin.esp");
        File.WriteAllBytes(sourceFile, [1, 2, 3, 4]);
        var sourceBefore = File.ReadAllBytes(sourceFile);
        var guard = new ReadOnlySourceGuard([area.Source], area.Workspace);
        var fileSystem = new WorkspaceFileSystem(guard);
        var destination = Path.Combine(area.Workspace, "config", "project.json");

        fileSystem.WriteAllTextAtomic(destination, "{\"schemaVersion\":1}");

        Assert.Equal("{\"schemaVersion\":1}", File.ReadAllText(destination));
        Assert.Equal(sourceBefore, File.ReadAllBytes(sourceFile));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp"));
    }

    [Fact]
    public void RejectsWorkspaceAndSourceOverlap()
    {
        using var area = new TestArea();

        Assert.Throws<SafetyViolationException>(() =>
            new ReadOnlySourceGuard([area.Root], area.Workspace));
    }

    [Fact]
    public void AtomicallyPromotesOnlyWorkspaceStagedFile()
    {
        using var area = new TestArea();
        var guard = new ReadOnlySourceGuard([area.Source], area.Workspace);
        var fileSystem = new WorkspaceFileSystem(guard);
        var staged = Path.Combine(area.Workspace, "index", "database.staged");
        var destination = Path.Combine(area.Workspace, "index", "database.sqlite");
        File.WriteAllText(fileSystem.PrepareFileDestination(staged), "new");

        fileSystem.ReplaceFileAtomic(staged, destination);

        Assert.False(File.Exists(staged));
        Assert.Equal("new", File.ReadAllText(destination));
        Assert.Throws<SafetyViolationException>(() =>
            fileSystem.ReplaceFileAtomic(destination, Path.Combine(area.Source, "database.sqlite")));
    }

    [Fact]
    public void CopiesFilesOnlyWithinWorkspace()
    {
        using var area = new TestArea();
        var guard = new ReadOnlySourceGuard([area.Source], area.Workspace);
        var fileSystem = new WorkspaceFileSystem(guard);
        var source = Path.Combine(area.Workspace, "index", "source.sqlite");
        var destination = Path.Combine(area.Workspace, "cache", "copy.sqlite");
        File.WriteAllText(fileSystem.PrepareFileDestination(source), "snapshot");

        fileSystem.CopyFileWithinWorkspace(source, destination);

        Assert.Equal("snapshot", File.ReadAllText(destination));
        Assert.Throws<SafetyViolationException>(() =>
            fileSystem.CopyFileWithinWorkspace(Path.Combine(area.Source, "source.sqlite"), destination + ".bad"));
    }

    [Fact]
    public void RejectsReparsePointEscapingWorkspaceWhenSupported()
    {
        using var area = new TestArea();
        var configDirectory = Path.Combine(area.Workspace, "config");
        try
        {
            Directory.CreateSymbolicLink(configDirectory, area.Source);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var guard = new ReadOnlySourceGuard([area.Source], area.Workspace);

        Assert.Throws<SafetyViolationException>(() =>
            guard.EnsureWritableDestination(Path.Combine(configDirectory, "escaped.txt")));
    }
}
