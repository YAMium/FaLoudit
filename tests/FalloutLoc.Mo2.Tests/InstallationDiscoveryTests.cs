using FalloutLoc.Core.Configuration;
using FalloutLoc.Core.IO;
using FalloutLoc.Mo2.Models;

namespace FalloutLoc.Mo2.Tests;

public sealed class InstallationDiscoveryTests
{
    [Fact]
    public void DiscoversPortableTtwProfileAndExcludesSeparators()
    {
        using var fixture = new Mo2Fixture();
        var fileSystem = CreateSourceFileSystem(fixture);

        var result = new InstallationDiscovery(fileSystem).Discover(fixture.Source);

        Assert.Equal(GameMode.TaleOfTwoWastelands, result.Mode);
        Assert.Equal("Test Profile", result.SelectedProfile);
        Assert.Equal(3, result.AvailableActualMods);
        Assert.Equal(2, result.Profile.EnabledActualMods);
        Assert.Equal(1, result.Profile.EnabledSeparators);
        Assert.Equal(3, result.Profile.ActivePlugins.Count);
        Assert.True(result.Profile.PluginAndLoadOrderMatch);
    }

    [Fact]
    public void EarlierModlistEntryWinsAmongEnabledMods()
    {
        using var fixture = new Mo2Fixture();
        var fileSystem = CreateSourceFileSystem(fixture);
        var profile = new Mo2ProfileReader(fileSystem).Read(fixture.ModsRoot, fixture.ProfileRoot);

        var resolution = new Mo2FileResolver(fileSystem)
            .Resolve("Menus/sample.xml", fixture.DataRoot, fixture.OverwriteRoot, profile);

        Assert.Equal(2, resolution.Providers.Count);
        Assert.Equal("Russian Mod", resolution.Winner?.SourceName);
        Assert.Equal(PhysicalSourceKind.Mod, resolution.Winner?.SourceKind);
        Assert.True(resolution.Providers[0].EffectivePriority > resolution.Providers[1].EffectivePriority);
    }

    [Fact]
    public void OverwriteWinsAboveEveryMod()
    {
        using var fixture = new Mo2Fixture(addOverwriteConflict: true);
        var fileSystem = CreateSourceFileSystem(fixture);
        var profile = new Mo2ProfileReader(fileSystem).Read(fixture.ModsRoot, fixture.ProfileRoot);

        var resolution = new Mo2FileResolver(fileSystem)
            .Resolve("Menus/sample.xml", fixture.DataRoot, fixture.OverwriteRoot, profile);

        Assert.Equal(3, resolution.Providers.Count);
        Assert.Equal(PhysicalSourceKind.Overwrite, resolution.Winner?.SourceKind);
        Assert.Equal("overwrite", resolution.Winner?.SourceName);
    }

    [Fact]
    public void ResolvesAllActivePluginProvidersWithOneDirectoryScan()
    {
        using var fixture = new Mo2Fixture();
        File.WriteAllText(Path.Combine(fixture.ModsRoot, "Base Mod", "FalloutNV.esm"), "base override");
        File.WriteAllText(Path.Combine(fixture.ModsRoot, "Russian Mod", "FalloutNV.esm"), "russian override");
        File.WriteAllText(Path.Combine(fixture.ModsRoot, "Disabled Mod", "FalloutNV.esm"), "disabled");
        var fileSystem = CreateSourceFileSystem(fixture);
        var profile = new Mo2ProfileReader(fileSystem).Read(fixture.ModsRoot, fixture.ProfileRoot);

        var map = new Mo2FileResolver(fileSystem).ResolvePluginMap(
            profile.ActivePlugins, fixture.DataRoot, fixture.OverwriteRoot, profile);

        Assert.Equal(3, map.Count);
        var falloutNv = map["FalloutNV.esm"];
        Assert.Equal("Russian Mod", falloutNv.Winner?.SourceName);
        Assert.Equal(3, falloutNv.Providers.Count);
        Assert.DoesNotContain(falloutNv.Providers, provider => provider.SourceName == "Disabled Mod");
    }

    [Fact]
    public void ResolvesWinningFilesRecursivelyUnderALogicalDirectory()
    {
        using var fixture = new Mo2Fixture();
        var relative = Path.Combine("NVSE", "Plugins", "Tweaks", "Gamesettings", "nested", "strings.ini");
        foreach (var (mod, content) in new[] { ("Base Mod", "base"), ("Russian Mod", "winner") })
        {
            var path = Path.Combine(fixture.ModsRoot, mod, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        var fileSystem = CreateSourceFileSystem(fixture);
        var profile = new Mo2ProfileReader(fileSystem).Read(fixture.ModsRoot, fixture.ProfileRoot);

        var map = new Mo2FileResolver(fileSystem).ResolveDirectoryFiles(
            Path.Combine("NVSE", "Plugins", "Tweaks", "Gamesettings"),
            ".ini",
            fixture.DataRoot,
            fixture.OverwriteRoot,
            profile);

        var resolution = Assert.Single(map).Value;
        Assert.Equal(2, resolution.Providers.Count);
        Assert.Equal("Russian Mod", resolution.Winner?.SourceName);
        Assert.EndsWith(relative, resolution.Winner?.PhysicalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsLogicalParentTraversal()
    {
        using var fixture = new Mo2Fixture();
        var fileSystem = CreateSourceFileSystem(fixture);
        var profile = new Mo2ProfileReader(fileSystem).Read(fixture.ModsRoot, fixture.ProfileRoot);
        var resolver = new Mo2FileResolver(fileSystem);

        Assert.Throws<ArgumentException>(() =>
            resolver.Resolve("../outside.txt", fixture.DataRoot, fixture.OverwriteRoot, profile));
    }

    [Fact]
    public void BootstrapIncludesExternalConfiguredRoots()
    {
        using var fixture = new Mo2Fixture();
        var externalGame = Path.Combine(fixture.Root, "external-game");
        var externalBase = Path.Combine(fixture.Root, "external-base");
        File.WriteAllText(
            Path.Combine(fixture.Source, "ModOrganizer.ini"),
            "[General]\n" +
            $"gamePath=@ByteArray({externalGame.Replace("\\", "\\\\", StringComparison.Ordinal)})\n" +
            "selected_profile=@ByteArray(Test Profile)\n" +
            "[Settings]\n" +
            $"base_directory={externalBase.Replace('\\', '/')}\n" +
            "mod_directory=%BASE_DIR%/mods\n");

        var roots = DiscoveryBootstrap.FindSourceRoots(fixture.Source);

        Assert.Contains(roots, path => PathRules.PathEquals(path, externalGame));
        Assert.Contains(roots, path => PathRules.PathEquals(path, externalBase));
        Assert.Contains(roots, path => PathRules.PathEquals(path, Path.Combine(externalBase, "mods")));
    }

    [Fact]
    public void MissingModlistEntriesDoNotConsumeMo2Priority()
    {
        using var fixture = new Mo2Fixture();
        File.WriteAllText(
            Path.Combine(fixture.ProfileRoot, "modlist.txt"),
            "# generated\n+Missing Mod\n+Russian Mod\n-Disabled Mod\n+Visual_separator\n+Base Mod\n");
        var fileSystem = CreateSourceFileSystem(fixture);

        var profile = new Mo2ProfileReader(fileSystem).Read(fixture.ModsRoot, fixture.ProfileRoot);
        var missing = Assert.Single(profile.ModEntries, entry => entry.Name == "Missing Mod");
        var russian = Assert.Single(profile.ModEntries, entry => entry.Name == "Russian Mod");

        Assert.False(missing.DirectoryExists);
        Assert.Equal(int.MinValue, missing.EffectivePriority);
        Assert.Equal(3, russian.EffectivePriority);
    }

    private static SourceFileSystem CreateSourceFileSystem(Mo2Fixture fixture)
    {
        var guard = new ReadOnlySourceGuard([fixture.Source], fixture.Workspace);
        return new SourceFileSystem(guard);
    }
}
