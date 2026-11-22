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
    public void ResolvesVirtualDataIniFilesFromRootAndExcludesMo2Metadata()
    {
        using var fixture = new Mo2Fixture();
        foreach (var mod in new[] { "Base Mod", "Russian Mod" })
        {
            var path = Path.Combine(fixture.ModsRoot, mod, "Config", "Example.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, mod);
            File.WriteAllText(Path.Combine(fixture.ModsRoot, mod, "meta.ini"), "[General]");
            File.WriteAllText(Path.Combine(fixture.ModsRoot, mod, "desktop.ini"), "[.ShellClassInfo]");
            var hidden = Path.Combine(fixture.ModsRoot, mod, "Config.mohidden", "Hidden.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(hidden)!);
            File.WriteAllText(hidden, "hidden");
        }

        var fileSystem = CreateSourceFileSystem(fixture);
        var profile = new Mo2ProfileReader(fileSystem).Read(fixture.ModsRoot, fixture.ProfileRoot);

        var map = new Mo2FileResolver(fileSystem).ResolveDirectoryFiles(
            ".", ".ini", fixture.DataRoot, fixture.OverwriteRoot, profile);

        var resolution = Assert.Single(map).Value;
        Assert.Equal(Path.Combine("Config", "Example.ini"), resolution.LogicalPath);
        Assert.Equal("Russian Mod", resolution.Winner?.SourceName);
        Assert.Equal(2, resolution.Providers.Count);
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

    [Fact]
    public void InventoryAndLiveResolverReturnIdenticalPathsStampsAndProviderOrder()
    {
        using var f = new Mo2Fixture(addOverwriteConflict: true);
        var fs = CreateSourceFileSystem(f);
        var profile = new Mo2ProfileReader(fs).Read(f.ModsRoot, f.ProfileRoot);
        var roots = profile.ModEntries.Where(m => m.Enabled && m.DirectoryExists && !m.IsSeparator)
            .Select(m => m.Directory).Append(f.DataRoot).Append(f.OverwriteRoot).ToArray();
        foreach (var root in roots)
        {
            foreach (var rel in new[] { "nvse/Plugins/Scripts/deep/s.txt", "NVSE/Plugins/Scripts/s.gek",
                         "Textures/Deep/a.ini", "menus/Extra.XML", "Hidden.mohidden/ignored.ini", "meta.ini" })
            {
                var file = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, root);
            }
        }
        var inventory = new IndexFileListing(fs, roots, profile.ActivePlugins);
        var cached = new Mo2FileResolver(inventory);
        var live = new Mo2FileResolver(fs);
        void Compare(IReadOnlyDictionary<string, PhysicalFileResolution> expected,
            IReadOnlyDictionary<string, PhysicalFileResolution> actual)
        {
            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
            foreach (var pair in expected)
            {
                var match = actual[pair.Key];
                Assert.Equal(pair.Value.LogicalPath, match.LogicalPath);
                Assert.Equal(pair.Value.Providers.ToArray(), match.Providers.ToArray());
                foreach (var p in match.Providers) Assert.Equal(fs.ReadStamp(p.PhysicalPath), inventory.ReadStamp(p.PhysicalPath));
            }
        }
        Compare(live.ResolvePluginMap(profile.ActivePlugins, f.DataRoot, f.OverwriteRoot, profile),
            cached.ResolvePluginMap(profile.ActivePlugins, f.DataRoot, f.OverwriteRoot, profile));
        foreach (var (dir, ext) in new[] { (".", ".ini"), ("Menus", ".xml"), ("NVSE/Plugins/Scripts", ".txt"),
                     ("NVSE/Plugins/Scripts", ".gek"), ("NVSE/CompileScript", ".gek") })
        {
            Compare(live.ResolveDirectoryFiles(dir, ext, f.DataRoot, f.OverwriteRoot, profile),
                cached.ResolveDirectoryFiles(dir, ext, f.DataRoot, f.OverwriteRoot, profile));
        }
    }

    [Fact]
    public void InventoryDoesNotFollowEscapingDirectoryOrFileLinksWhenSupported()
    {
        using var f = new Mo2Fixture();
        var outside = Path.Combine(f.Workspace, "external.ini");
        File.WriteAllText(outside, "external");
        var file = Path.Combine(f.DataRoot, "link.ini");
        var dir = Path.Combine(f.DataRoot, "alias");
        try
        {
            Directory.CreateSymbolicLink(dir, f.Workspace);
            File.CreateSymbolicLink(file, outside);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        var listing = new IndexFileListing(CreateSourceFileSystem(f), [f.DataRoot], []);
        Assert.Throws<SafetyViolationException>(() => listing.EnumerateFiles(dir).ToArray());
        Assert.Throws<SafetyViolationException>(() => listing.ReadStamp(file));
        Assert.Throws<SafetyViolationException>(() => listing.FileExists(Path.Combine(dir, "external.ini")));
    }

    private static SourceFileSystem CreateSourceFileSystem(Mo2Fixture fixture)
    {
        var guard = new ReadOnlySourceGuard([fixture.Source], fixture.Workspace);
        return new SourceFileSystem(guard);
    }
}
