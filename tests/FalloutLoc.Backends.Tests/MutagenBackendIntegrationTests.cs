using FalloutLoc.Backends.Encoding;
using FalloutLoc.Backends.Models;
using FalloutLoc.Backends.Mutagen;
using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Backends.Tests;

public sealed class MutagenBackendIntegrationTests
{
    [Fact]
    public void LocalizationFieldCatalogDeclaresComplexFieldsAndLeavesScriptsUnverified()
    {
        Assert.Equal("1", LocalizationFieldCatalog.Version);
        Assert.Contains(LocalizationFieldCatalog.SupportedFields, field =>
            field.RecordType == "Quest" && field.Category == "quest-objective");
        Assert.Contains(LocalizationFieldCatalog.SupportedFields, field =>
            field.RecordType == "PlacedObject" && field.SemanticPathPattern == "MapMarker.Name");
        Assert.DoesNotContain(LocalizationFieldCatalog.SupportedFields, field => field.RecordType == "Script");
        Assert.DoesNotContain("Script", LocalizationFieldCatalog.AuditedNonLocalizedRecordTypes);
    }

    [Fact]
    public void ReadsKnownTtwOverrideWithStableFormKeyAndCp1251Text()
    {
        var data = GetCopiedOracleDataOrNull();
        if (data is null)
        {
            return;
        }

        var backend = CreateBackend();
        using var session = backend.Open(new PluginOpenRequest
        {
            Path = Path.Combine(data, "YUPTTW.esm"),
            Mode = GameMode.TaleOfTwoWastelands,
            LoadOrderIndex = 1,
            SourceMod = "oracle-copy",
        });

        var record = Assert.Single(session.EnumerateMajorRecords(), record =>
            record.FormKey.Equals("00CEE9:LonesomeRoad.esm", StringComparison.OrdinalIgnoreCase));
        var name = Assert.Single(record.Strings, field => field.SemanticPath == "Name");

        Assert.Equal("YUPTTW.esm", session.Metadata.PluginName);
        Assert.Contains("LonesomeRoad.esm", session.Metadata.Masters);
        Assert.Equal("NVDLC04RushingWaterBE", record.EditorId);
        Assert.Equal(RecordParseStatus.Parsed, record.ParseStatus);
        Assert.Equal("+50% к скор. атаки", name.Text);
        Assert.Equal(StringEncodingEvidence.Windows1251Recovered, name.EncodingEvidence);

        var script = session.EnumerateMajorRecords().FirstOrDefault(candidate =>
            candidate.RecordType == "Script" && candidate.Contents.Count > 0);
        Assert.NotNull(script);
        Assert.Equal(RecordParseStatus.PartiallyParsed, script.ParseStatus);
        Assert.Contains(script.ParseWarnings, warning =>
            warning.Contains("untrusted content", StringComparison.OrdinalIgnoreCase));
        var source = Assert.Single(script.Contents);
        Assert.Equal("Fields.SourceCode", source.SemanticPath);
        Assert.Equal(RecordContentSourceKind.EmbeddedScriptSource, source.SourceKind);
        Assert.False(source.IsHeuristic);
        Assert.True(source.RequiresGptReview);
        Assert.False(string.IsNullOrWhiteSpace(source.Text));

        var nested = session.EnumerateMajorRecords()
            .SelectMany(candidate => candidate.Contents)
            .FirstOrDefault(content => content.SemanticPath != "Fields.SourceCode");
        Assert.NotNull(nested);
        Assert.Contains(".SourceCode", nested.SemanticPath, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(nested.Text));
    }

    [Fact]
    public void ProducesBackendNeutralOverrideChainAndWinner()
    {
        var data = GetCopiedOracleDataOrNull();
        if (data is null)
        {
            return;
        }

        var plugins = new[]
        {
            new ActivePluginSource
            {
                LoadOrderIndex = 0,
                PluginName = "LonesomeRoad.esm",
                PhysicalPath = Path.Combine(data, "LonesomeRoad.esm"),
                SourceMod = "oracle-copy",
            },
            new ActivePluginSource
            {
                LoadOrderIndex = 1,
                PluginName = "YUPTTW.esm",
                PhysicalPath = Path.Combine(data, "YUPTTW.esm"),
                SourceMod = "oracle-copy",
            },
        };
        var resolver = new MutagenOverrideResolver(CreateBackend());

        var result = resolver.Trace(new OverrideTraceRequest
        {
            Mode = GameMode.TaleOfTwoWastelands,
            FormKey = "00CEE9:LonesomeRoad.esm",
            ActivePlugins = plugins,
        });

        Assert.Equal(2, result.Chain.Count);
        Assert.Equal("LonesomeRoad.esm", result.Chain[0].Plugin.PluginName);
        Assert.Equal("YUPTTW.esm", result.Winner?.Plugin.PluginName);
        Assert.Equal(
            "Энергетический напиток",
            Assert.Single(result.Chain[0].Record.Strings, field => field.SemanticPath == "Name").Text);
        Assert.Equal(
            "+50% к скор. атаки",
            Assert.Single(result.Winner!.Record.Strings, field => field.SemanticPath == "Name").Text);
    }

    [Fact]
    public void ExtractsValidatedNestedStringFieldsWithSemanticIdentity()
    {
        var data = GetCopiedOracleDataOrNull();
        if (data is null)
        {
            return;
        }

        var backend = CreateBackend();
        using var infoSession = backend.Open(Request(data, "Better Brotherhood.esm"));
        var info = Assert.Single(infoSession.EnumerateMajorRecords(), record =>
            record.FormKey.Equals("0CE224:FalloutNV.esm", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(info.Strings, field =>
            field.Category == "dialogue-response"
            && field.SemanticPath.StartsWith("Responses[number=", StringComparison.Ordinal)
            && field.Text == "Now, who can tell me the primary components of gunpowder?");

        using var terminalSession = backend.Open(Request(data, "Hope Lies - AiO Improved.esp"));
        var terminal = Assert.Single(terminalSession.EnumerateMajorRecords(), record =>
            record.FormKey.Equals("0186FA:3DNPCFNVBundle.esm", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(terminal.Strings, field =>
            field.Category == "terminal-menu" && field.Text == "Project Report Alpha");
        Assert.Contains(terminal.Strings, field =>
            field.Category == "terminal-result" && field.Text == "Loading...");

        using var bodySession = backend.Open(Request(data, "New Blood TTW Patch.esp"));
        var body = Assert.Single(bodySession.EnumerateMajorRecords(), record =>
            record.FormKey.Equals("0051F8:Anchorage.esm", StringComparison.OrdinalIgnoreCase));
        var bodyNames = body.Strings.Where(field => field.Category == "body-part-name").ToArray();
        Assert.Equal(7, bodyNames.Length);
        Assert.All(bodyNames, field => Assert.Contains("actorValue=", field.SemanticPath));
        Assert.Contains(bodyNames, field => field.Text == "Left Track");
        Assert.Contains(bodyNames, field => field.Text == "Power Generator");
    }

    private static MutagenPluginBackend CreateBackend() => new(new StrictPluginStringDecoder());

    private static PluginOpenRequest Request(string data, string plugin) => new()
    {
        Path = Path.Combine(data, plugin),
        Mode = GameMode.TaleOfTwoWastelands,
        LoadOrderIndex = 0,
        SourceMod = "oracle-copy",
    };

    private static string? GetCopiedOracleDataOrNull()
    {
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        var data = Path.Combine(projectRoot, ".falloutloc", "cache", "xedit-lab", "data");
        return File.Exists(Path.Combine(data, "LonesomeRoad.esm"))
            && File.Exists(Path.Combine(data, "YUPTTW.esm"))
            ? data
            : null;
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
