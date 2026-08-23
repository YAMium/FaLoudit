namespace FalloutLoc.Mo2.Tests;

internal sealed class Mo2Fixture : IDisposable
{
    public Mo2Fixture(bool addOverwriteConflict = false)
    {
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        Root = Path.Combine(projectRoot, ".falloutloc", "fixtures", "production-tests", Guid.NewGuid().ToString("N"));
        Source = Path.Combine(Root, "source");
        Workspace = Path.Combine(Root, "workspace");
        GameRoot = Path.Combine(Source, "game");
        DataRoot = Path.Combine(GameRoot, "Data");
        ModsRoot = Path.Combine(Source, "mods");
        ProfilesRoot = Path.Combine(Source, "profiles");
        OverwriteRoot = Path.Combine(Source, "overwrite");
        ProfileRoot = Path.Combine(ProfilesRoot, "Test Profile");

        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ModsRoot);
        Directory.CreateDirectory(ProfileRoot);
        Directory.CreateDirectory(OverwriteRoot);
        Directory.CreateDirectory(Workspace);
        File.WriteAllBytes(Path.Combine(Source, "ModOrganizer.exe"), []);
        File.WriteAllText(
            Path.Combine(Source, "ModOrganizer.ini"),
            "[General]\n" +
            "gameName=New Vegas\n" +
            $"gamePath=@ByteArray({GameRoot.Replace("\\", "\\\\", StringComparison.Ordinal)})\n" +
            "selected_profile=@ByteArray(Test Profile)\n" +
            "version=2.5.2\n");

        foreach (var name in new[] { "Russian Mod", "Disabled Mod", "Visual_separator", "Base Mod" })
        {
            Directory.CreateDirectory(Path.Combine(ModsRoot, name));
        }

        File.WriteAllText(
            Path.Combine(ProfileRoot, "modlist.txt"),
            "# generated\n+Russian Mod\n-Disabled Mod\n+Visual_separator\n+Base Mod\n");
        var plugins = "# generated\nFalloutNV.esm\nFallout3.esm\nTaleOfTwoWastelands.esm\n";
        File.WriteAllText(Path.Combine(ProfileRoot, "plugins.txt"), plugins);
        File.WriteAllText(Path.Combine(ProfileRoot, "loadorder.txt"), plugins);

        File.WriteAllText(Path.Combine(DataRoot, "FalloutNV.esm"), "game");
        File.WriteAllText(Path.Combine(ModsRoot, "Russian Mod", "Fallout3.esm"), "russian");
        File.WriteAllText(Path.Combine(ModsRoot, "Base Mod", "TaleOfTwoWastelands.esm"), "ttw");
        WriteLogical(Path.Combine(ModsRoot, "Russian Mod"), "Menus/sample.xml", "winner");
        WriteLogical(Path.Combine(ModsRoot, "Base Mod"), "Menus/sample.xml", "loser");
        if (addOverwriteConflict)
        {
            WriteLogical(OverwriteRoot, "Menus/sample.xml", "overwrite");
        }
    }

    public string Root { get; }

    public string Source { get; }

    public string Workspace { get; }

    public string GameRoot { get; }

    public string DataRoot { get; }

    public string ModsRoot { get; }

    public string ProfilesRoot { get; }

    public string OverwriteRoot { get; }

    public string ProfileRoot { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static void WriteLogical(string root, string logicalPath, string content)
    {
        var path = Path.Combine(root, logicalPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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
