namespace FalloutLoc.Core.Tests;

internal sealed class TestArea : IDisposable
{
    public TestArea()
    {
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        Root = Path.Combine(projectRoot, ".falloutloc", "fixtures", "production-tests", Guid.NewGuid().ToString("N"));
        Source = Path.Combine(Root, "source");
        Workspace = Path.Combine(Root, "workspace");
        Directory.CreateDirectory(Source);
        Directory.CreateDirectory(Workspace);
    }

    public string Root { get; }

    public string Source { get; }

    public string Workspace { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static string FindProjectRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the project root from the test output directory.");
    }
}
