using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Encoding;
using FalloutLoc.Backends.Models;
using FalloutLoc.Core.Configuration;
using FalloutLoc.Core.IO;
using FalloutLoc.Index.Models;
using Microsoft.Data.Sqlite;

namespace FalloutLoc.Index.Tests;

public sealed class SqliteIndexTests
{
    [Fact]
    public void BundledSqliteMeetsSecurityBaseline()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var versionText = Assert.IsType<string>(command.ExecuteScalar());
        var version = Version.Parse(versionText);

        Assert.True(version >= new Version(3, 50, 2),
            $"Bundled SQLite {version} is older than the 3.50.2 security baseline.");
    }

    [Fact]
    public void BuildsSearchableIndexAndResolvesWinningOverride()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] = [Record("000123:Base.esm", "Old Russian", "Старый русский текст")],
            ["patch.esp"] = [Record("000123:Base.esm", "New English", "New English text")],
        };
        var result = CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));

        Assert.Equal(2, result.ParsedPlugins);
        Assert.Equal(0, result.FailedPlugins);
        Assert.Equal(2, result.Records);
        var repository = new SqliteIndexRepository(area.Database);
        var found = Assert.Single(repository.Find("русский"));
        Assert.False(found.IsWinningOverride);
        Assert.Equal(TextLanguageKind.Russian, found.Language);

        var trace = repository.Trace("000123:base.esm");
        Assert.Equal(2, trace.Chain.Count);
        Assert.False(trace.Chain[0].IsWinner);
        Assert.True(trace.Chain[1].IsWinner);
        Assert.Equal("Patch Mod", trace.Chain[1].SourceMod);
        Assert.Equal("New English text", Assert.Single(trace.Chain[1].Strings).Text);
        Assert.Equal(["000123:Base.esm"], repository.FindRegressionCandidateFormKeys(null, 10));
        var status = repository.GetStatus();
        Assert.Equal(2, status.ParsedPlugins);
        Assert.Equal(0, status.FailedPlugins);
        Assert.Equal("en", status.SourceLanguage);
        Assert.Equal("ru", status.TargetLanguage);
        Assert.Equal("ok", repository.CheckIntegrity());
    }

    [Fact]
    public void EngineGameSettingsJoinPluginAndPostPluginOverridesByEditorId()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] =
            [
                Record(
                    "000999:Base.esm",
                    "sHowMany",
                    "Сколько из плагина?",
                    recordType: "GameSettingString",
                    category: "game-setting") with
                {
                    Strings =
                    [
                        new RecordStringOccurrence
                        {
                            SemanticPath = "Data",
                            Category = "game-setting",
                            Text = "Сколько из плагина?",
                            Language = TextLanguageKind.Target,
                            EncodingEvidence = StringEncodingEvidence.UnicodeTarget,
                            Ambiguous = false,
                        },
                    ],
                },
            ],
        };
        var request = Request(area, "base.esm") with
        {
            EngineGameSettingCatalogStatus = "extracted",
            EngineGameSettingCatalogPath = Path.Combine(area.Source, "GECK.exe"),
            RuntimeExecutablePath = Path.Combine(area.Source, "FalloutNV.exe"),
            EngineGameSettings =
            [
                new IndexEngineGameSettingInput
                {
                    EditorId = "sHowMany",
                    DefaultText = "How many?",
                    Language = TextLanguageKind.Source,
                    EncodingEvidence = StringEncodingEvidence.Ascii,
                    Ambiguous = false,
                },
            ],
            PostPluginGameSettings =
            [
                new IndexPostPluginGameSettingInput
                {
                    EditorId = "sHowMany",
                    Text = "Сколько из INI?",
                    Language = TextLanguageKind.Target,
                    EncodingEvidence = StringEncodingEvidence.UnicodeTarget,
                    Ambiguous = false,
                    LogicalPath = "NVSE/Plugins/nvse_stewie_tweaks.ini",
                    PhysicalPath = Path.Combine(area.Source, "nvse_stewie_tweaks.ini"),
                    SourceMod = "Stewie INI",
                    EffectivePriority = 10,
                    Sequence = 0,
                },
            ],
        };

        var result = CreateBuilder(area, new FakeBackend(records)).Build(request);
        var repository = new SqliteIndexRepository(area.Database);

        Assert.Equal(1, result.EngineGameSettings);
        Assert.Equal(1, result.PostPluginGameSettingOverrides);
        var engineMatch = Assert.Single(repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "How many?",
            Mode = IndexedTextSearchMode.Exact,
        }).Items);
        Assert.Equal("gmst:sHowMany", engineMatch.FormKey);
        Assert.Equal("engineDefault", engineMatch.SourceKind);
        Assert.False(engineMatch.IsWinningOverride);

        var trace = repository.Trace("gmst:sHowMany");
        Assert.Equal(3, trace.Chain.Count);
        Assert.Equal("FalloutNV.exe", trace.Chain[0].PluginName);
        Assert.Equal("base.esm", trace.Chain[1].PluginName);
        Assert.Equal("NVSE/Plugins/nvse_stewie_tweaks.ini", trace.Chain[2].PluginName);
        Assert.True(trace.Chain[2].IsWinner);
        Assert.Equal("Сколько из INI?", Assert.Single(trace.Chain[2].Strings).Text);

        var editorMatch = Assert.Single(repository.FindByEditorId(new IndexedEditorIdSearchRequest
        {
            EditorId = "SHOWMANY",
        }).Items);
        Assert.Equal("gmst:sHowMany", editorMatch.FormKey);
        Assert.Equal(3, editorMatch.OverrideCount);
        var status = repository.GetStatus();
        Assert.Equal("extracted", status.EngineGameSettingCatalogStatus);
        Assert.Equal(1, status.EngineGameSettings);
        Assert.Equal(1, status.PostPluginGameSettingOverrides);
    }

    [Fact]
    public void RegressionCandidatesIncludeExactSourceReversionAcrossSharedLatinScript()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] = [Record("000123:Base.esm", "Base", "Armor")],
            ["polish.esp"] = [Record("000123:Base.esm", "Polish", "Pancerz")],
            ["patch.esp"] = [Record("000123:Base.esm", "Patch", "Armor")],
        };
        CreateBuilder(area, new FakeBackend(records)).Build(
            Request(area, "base.esm", "polish.esp", "patch.esp") with
            {
                SourceLanguage = "en",
                TargetLanguage = "pl",
            });

        var candidates = new SqliteIndexRepository(area.Database)
            .FindRegressionCandidateFormKeys(null, 10);

        Assert.Equal(["000123:Base.esm"], candidates);
    }

    [Fact]
    public void RegressionCandidatesDoNotInferAnEmptyIntermediateTranslation()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] = [Record("000123:Base.esm", "Base", "Armor")],
            ["empty.esp"] = [Record("000123:Base.esm", "Empty", "")],
            ["patch.esp"] = [Record("000123:Base.esm", "Patch", "Armor")],
        };
        CreateBuilder(area, new FakeBackend(records)).Build(
            Request(area, "base.esm", "empty.esp", "patch.esp"));

        Assert.Empty(new SqliteIndexRepository(area.Database)
            .FindRegressionCandidateFormKeys(null, 10));
    }

    [Fact]
    public void SearchApiAppliesModesFiltersAndQueryBoundPagination()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] =
            [
                Record("000123:Base.esm", "TargetItem", "Alpha Text"),
                Record("000124:Base.esm", "SecondItem", "alpha text extra"),
            ],
            ["patch.esp"] =
            [
                Record("000123:Base.esm", "TargetItem", "Alpha Text"),
                Record("ABCDEF:Patch.esp", "TargetItem", "Unique Armor", "Patch.esp", "Armor", "item-name"),
            ],
        };
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var repository = new SqliteIndexRepository(area.Database);

        var caseSensitive = repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "Alpha",
            Limit = 10,
        });
        Assert.Equal(2, caseSensitive.Items.Count);

        var ignoreCase = repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "alpha",
            IgnoreCase = true,
            Limit = 10,
        });
        Assert.Equal(3, ignoreCase.Items.Count);

        var filtered = repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "Alpha Text",
            Mode = IndexedTextSearchMode.Exact,
            PluginName = "PATCH.ESP",
            RecordType = "activator",
            Category = "DISPLAY-NAME",
            WinnerOnly = true,
            Limit = 10,
        });
        Assert.Equal("patch.esp", Assert.Single(filtered.Items).PluginName);

        var regex = repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "^unique\\s+armor$",
            Mode = IndexedTextSearchMode.Regex,
            IgnoreCase = true,
            Limit = 10,
        });
        Assert.Equal("ABCDEF:Patch.esp", Assert.Single(regex.Items).FormKey);

        var firstPage = repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "text",
            IgnoreCase = true,
            Limit = 1,
        });
        Assert.True(firstPage.HasMore);
        Assert.NotNull(firstPage.NextCursor);
        var secondPage = repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "text",
            IgnoreCase = true,
            Limit = 1,
            Cursor = firstPage.NextCursor,
        });
        Assert.NotEqual(firstPage.Items[0], secondPage.Items[0]);
        Assert.Throws<ArgumentException>(() => repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "different",
            IgnoreCase = true,
            Limit = 1,
            Cursor = firstPage.NextCursor,
        }));
        Assert.Throws<ArgumentException>(() => repository.SearchText(new IndexedTextSearchRequest
        {
            Query = "[",
            Mode = IndexedTextSearchMode.Regex,
        }));
    }

    [Fact]
    public void IndexesEmbeddedScriptContentSeparatelyAndReturnsBoundedUntrustedContext()
    {
        using var area = new TestArea();
        var script = EmptyRecord(
            "000777:Base.esm",
            "Script",
            RecordParseStatus.PartiallyParsed,
            "Saved source is indexed as untrusted content.") with
        {
            EditorId = "TestConversationScript",
            Contents =
            [
                new RecordContentOccurrence
                {
                    SemanticPath = "Fields.SourceCode",
                    SourceKind = RecordContentSourceKind.EmbeddedScriptSource,
                    Text = """
                        scn TestConversationScript
                        begin GameMode
                          ShowMessage "Welcome to the Wasteland"
                        end
                        """,
                    EncodingEvidence = StringEncodingEvidence.Ascii,
                    Ambiguous = false,
                    IsHeuristic = false,
                },
            ],
        };
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] = [script],
        };

        var result = CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm"));
        Assert.Equal(6, result.SchemaVersion);
        Assert.Equal(1, result.Contents);
        Assert.Empty(new SqliteIndexRepository(area.Database).Find("Welcome"));

        var repository = new SqliteIndexRepository(area.Database);
        var match = Assert.Single(repository.SearchContent(new IndexedContentSearchRequest
        {
            Query = "welcome TO",
            IgnoreCase = true,
            WinnerOnly = true,
        }).Items);
        Assert.Equal("000777:Base.esm", match.FormKey);
        Assert.Equal("TestConversationScript", match.EditorId);
        Assert.Equal(RecordContentSourceKind.EmbeddedScriptSource, match.SourceKind);
        Assert.Contains("Welcome to the Wasteland", match.Context);
        Assert.True(match.IsWinningOverride);
        Assert.True(match.IsUntrustedContent);
        Assert.True(match.RequiresGptReview);
        Assert.False(match.IsHeuristic);
        Assert.InRange(match.Context.Length, 1, 1200);

        var reuseBackend = new FakeBackend(records);
        var reuse = CreateBuilder(area, reuseBackend).Build(Request(area, "base.esm"));
        Assert.Equal(1, reuse.ReusedPlugins);
        Assert.Equal(1, reuse.Contents);
        Assert.Equal(0, reuseBackend.OpenCount);
        Assert.Single(new SqliteIndexRepository(area.Database).SearchContent(new IndexedContentSearchRequest
        {
            Query = "Welcome",
            Mode = IndexedTextSearchMode.Regex,
        }).Items);
    }
    [Fact]
    public void DiagnosticCandidatesApplyFiltersAndQueryBoundPagination()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] =
            [
                Record("000123:Base.esm", "First", "Первый"),
                Record("000124:Base.esm", "Second", "Второй", recordType: "Armor", category: "item-name"),
            ],
            ["patch.esp"] =
            [
                Record("000123:Base.esm", "First", "First"),
                Record("000124:Base.esm", "Second", "Second", recordType: "Armor", category: "item-name"),
            ],
        };
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var repository = new SqliteIndexRepository(area.Database);

        var first = repository.FindDiagnosticCandidateFormKeys(new IndexedDiagnosticCandidateRequest
        {
            Kind = IndexedDiagnosticKind.Regressions,
            SourceMod = "Patch Mod",
            Limit = 1,
        });
        var second = repository.FindDiagnosticCandidateFormKeys(new IndexedDiagnosticCandidateRequest
        {
            Kind = IndexedDiagnosticKind.Regressions,
            SourceMod = "Patch Mod",
            Limit = 1,
            Cursor = first.NextCursor,
        });
        var armor = repository.FindDiagnosticCandidateFormKeys(new IndexedDiagnosticCandidateRequest
        {
            Kind = IndexedDiagnosticKind.Regressions,
            RecordType = "armor",
            Category = "ITEM-NAME",
            Limit = 10,
        });

        Assert.True(first.HasMore);
        Assert.NotEqual(Assert.Single(first.Items), Assert.Single(second.Items));
        Assert.Equal("000124:Base.esm", Assert.Single(armor.Items));
        Assert.Throws<ArgumentException>(() => repository.FindDiagnosticCandidateFormKeys(
            new IndexedDiagnosticCandidateRequest
            {
                Kind = IndexedDiagnosticKind.Untranslated,
                SourceMod = "Patch Mod",
                Limit = 1,
                Cursor = first.NextCursor,
            }));
    }

    [Fact]
    public void AddressSearchResolvesEditorIdRuntimeFormIdFormKeyAndAmbiguousLocalId()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] =
            [
                Record("000123:Base.esm", "TargetItem", "Base value"),
                Record("ABCDEF:Base.esm", "BaseDuplicate", "Base duplicate"),
            ],
            ["patch.esp"] =
            [
                Record("000123:Base.esm", "TargetItem", "Winning value"),
                Record("ABCDEF:Patch.esp", "TargetItem", "Patch duplicate", "Patch.esp"),
            ],
        };
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var repository = new SqliteIndexRepository(area.Database);

        var editorMatches = repository.FindByEditorId(new IndexedEditorIdSearchRequest
        {
            EditorId = "targetitem",
            Limit = 10,
        });
        Assert.Equal(2, editorMatches.Items.Count);
        var overridden = Assert.Single(editorMatches.Items, item => item.FormKey == "000123:Base.esm");
        Assert.Equal("patch.esp", overridden.WinningPluginName);
        Assert.Equal(2, overridden.OverrideCount);

        var onlyWinningBaseOccurrence = repository.FindByEditorId(new IndexedEditorIdSearchRequest
        {
            EditorId = "TargetItem",
            PluginName = "base.esm",
            WinnerOnly = true,
        });
        Assert.Empty(onlyWinningBaseOccurrence.Items);

        var baseRuntime = repository.ResolveForm("00ABCDEF");
        Assert.Equal(IndexedFormLookupKind.RuntimeFormId, baseRuntime.Kind);
        Assert.Equal("base.esm", baseRuntime.ResolvedOriginPlugin);
        Assert.Equal("ABCDEF:Base.esm", Assert.Single(baseRuntime.Matches.Items).FormKey);

        var patchRuntime = repository.ResolveForm("01ABCDEF");
        Assert.Equal("patch.esp", patchRuntime.ResolvedOriginPlugin);
        Assert.Equal("ABCDEF:Patch.esp", Assert.Single(patchRuntime.Matches.Items).FormKey);

        var canonical = repository.ResolveForm("abcdef:PATCH.ESP");
        Assert.Equal(IndexedFormLookupKind.FormKey, canonical.Kind);
        Assert.Equal("ABCDEF:Patch.esp", Assert.Single(canonical.Matches.Items).FormKey);

        var local = repository.ResolveForm("ABCDEF");
        Assert.Equal(IndexedFormLookupKind.LocalFormId, local.Kind);
        Assert.True(local.IsAmbiguous);
        Assert.Equal(2, local.Matches.Items.Count);
    }

    [Fact]
    public void PublishesCompleteIndexWithPerPluginFailureRecorded()
    {
        using var area = new TestArea();
        var backend = new FakeBackend(new Dictionary<string, IReadOnlyList<RecordOccurrence>>
        {
            ["base.esm"] = [Record("000123:Base.esm", "Base", "Text")],
        });

        var result = CreateBuilder(area, backend).Build(Request(area, "base.esm", "missing.esp"));

        Assert.Equal(1, result.ParsedPlugins);
        Assert.Equal(1, result.FailedPlugins);
        Assert.Single(new SqliteIndexRepository(area.Database).Find("Text"));
    }

    [Fact]
    public void CoverageReportExposesPartialUnverifiedAndNotApplicableRecords()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>
        {
            ["base.esm"] =
            [
                Record("000123:Base.esm", "Covered", "Visible text"),
                EmptyRecord("000124:Base.esm", "NavigationMesh", RecordParseStatus.NotApplicable),
                EmptyRecord("000125:Base.esm", "Script", RecordParseStatus.Unverified,
                    "Compiled script strings are not extracted."),
                EmptyRecord("000126:Base.esm", "Quest", RecordParseStatus.PartiallyParsed,
                    "Fixture nested field could not be read."),
            ],
        };

        var result = CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm"));
        var repository = new SqliteIndexRepository(area.Database);
        var status = repository.GetStatus();
        var coverage = repository.GetCoverage();

        Assert.Equal(6, result.SchemaVersion);
        Assert.Equal(1, result.PartiallyParsedPlugins);
        Assert.Equal(2, result.CoverageGapRecords);
        Assert.Equal(1, status.PartiallyParsedPlugins);
        Assert.Equal(2, status.CoverageGapRecords);
        Assert.Equal(1, coverage.ParsedRecords);
        Assert.Equal(1, coverage.PartiallyParsedRecords);
        Assert.Equal(1, coverage.NotApplicableRecords);
        Assert.Equal(1, coverage.UnverifiedRecords);
        Assert.Equal(2, coverage.Issues.Count);
        Assert.Contains(coverage.Issues, issue =>
            issue.Status == RecordParseStatus.Unverified
            && issue.Warnings.Contains("Compiled script strings are not extracted."));
    }

    [Fact]
    public void IncrementalReusePreservesPartialCoverageStatus()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>
        {
            ["base.esm"] =
            [
                EmptyRecord("000125:Base.esm", "Script", RecordParseStatus.Unverified,
                    "Compiled script strings are not extracted."),
            ],
        };
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm"));

        var reuseBackend = new FakeBackend(records);
        var result = CreateBuilder(area, reuseBackend).Build(Request(area, "base.esm"));

        Assert.Equal(1, result.ReusedPlugins);
        Assert.Equal(1, result.PartiallyParsedPlugins);
        Assert.Equal(1, result.CoverageGapRecords);
        Assert.Equal(0, reuseBackend.OpenCount);
        Assert.Equal(1, new SqliteIndexRepository(area.Database).GetCoverage().UnverifiedRecords);
    }

    [Fact]
    public void CancelledBuildPreservesPreviousPublishedDatabase()
    {
        using var area = new TestArea();
        File.WriteAllText(area.Database, "previous");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => CreateBuilder(area, new FakeBackend(
                new Dictionary<string, IReadOnlyList<RecordOccurrence>>()))
            .Build(Request(area, "base.esm"), cancellationToken: cancellation.Token));
        Assert.Equal("previous", File.ReadAllText(area.Database));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(area.Database)!, "*.staged"));
    }

    [Fact]
    public void FreshnessDetectsFreshStaleAndMissingSnapshots()
    {
        using var area = new TestArea();
        var builder = CreateBuilder(area, new FakeBackend(new Dictionary<string, IReadOnlyList<RecordOccurrence>>
        {
            ["base.esm"] = [Record("000123:Base.esm", "Base", "Text")],
        }));
        builder.Build(Request(area, "base.esm"));

        Assert.Equal(IndexFreshnessKind.Fresh,
            IndexFreshnessEvaluator.Evaluate(area.Database, "ABC", builder.CacheIdentity).Kind);
        Assert.Equal(IndexFreshnessKind.Stale,
            IndexFreshnessEvaluator.Evaluate(area.Database, "CHANGED").Kind);
        Assert.Equal(IndexFreshnessKind.Incompatible,
            IndexFreshnessEvaluator.Evaluate(area.Database, "ABC", "different-backend").Kind);
        Assert.Equal(IndexFreshnessKind.Missing,
            IndexFreshnessEvaluator.Evaluate(Path.Combine(area.Workspace, "index", "missing.sqlite"), "ABC").Kind);
    }

    [Fact]
    public void RebuildReusesAllUnchangedPluginsWithoutOpeningBackend()
    {
        using var area = new TestArea();
        var records = TwoPluginRecords();
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var reuseBackend = new FakeBackend(records);

        var result = CreateBuilder(area, reuseBackend).Build(Request(area, "base.esm", "patch.esp"));

        Assert.Equal(2, result.ReusedPlugins);
        Assert.Equal(0, result.ParsedPlugins);
        Assert.Equal(0, reuseBackend.OpenCount);
        Assert.Equal(2, new SqliteIndexRepository(area.Database).Trace("000123:Base.esm").Chain.Count);
    }

    [Fact]
    public void RebuildParsesOnlyPluginWhoseMetadataChanged()
    {
        using var area = new TestArea();
        var records = TwoPluginRecords();
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var changed = Request(area, "base.esm", "patch.esp");
        changed = changed with
        {
            Plugins = changed.Plugins.Select(plugin => plugin.Name == "patch.esp"
                ? plugin with { FileLength = plugin.FileLength + 1 }
                : plugin).ToArray(),
        };
        var incrementalBackend = new FakeBackend(records);

        var result = CreateBuilder(area, incrementalBackend).Build(changed);

        Assert.Equal(1, result.ReusedPlugins);
        Assert.Equal(1, result.ParsedPlugins);
        Assert.Equal(1, incrementalBackend.OpenCount);
    }

    [Fact]
    public void CacheIdentityChangeInvalidatesEveryPlugin()
    {
        using var area = new TestArea();
        var records = TwoPluginRecords();
        CreateBuilder(area, new FakeBackend(records, "backend-v1")).Build(Request(area, "base.esm", "patch.esp"));
        var changedBackend = new FakeBackend(records, "backend-v2");

        var result = CreateBuilder(area, changedBackend).Build(Request(area, "base.esm", "patch.esp"));

        Assert.Equal(0, result.ReusedPlugins);
        Assert.Equal(2, result.ParsedPlugins);
        Assert.Equal(2, changedBackend.OpenCount);
    }

    [Fact]
    public void LegacySchemaIsRebuiltInsteadOfReused()
    {
        using var area = new TestArea();
        var records = new Dictionary<string, IReadOnlyList<RecordOccurrence>>
        {
            ["base.esm"] = [Record("000123:Base.esm", "Base", "Text")],
        };
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm"));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = area.Database,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_info SET version = 1;";
            command.ExecuteNonQuery();
        }

        var migrationBackend = new FakeBackend(records);
        var result = CreateBuilder(area, migrationBackend).Build(Request(area, "base.esm"));

        Assert.Equal(0, result.ReusedPlugins);
        Assert.Equal(1, result.ParsedPlugins);
        Assert.Equal(1, migrationBackend.OpenCount);
        Assert.Equal(6, new SqliteIndexRepository(area.Database).GetStatus().SchemaVersion);
    }

    [Fact]
    public void DisabledReuseReparsesEveryPlugin()
    {
        using var area = new TestArea();
        var records = TwoPluginRecords();
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var reparseBackend = new FakeBackend(records);
        var request = Request(area, "base.esm", "patch.esp") with { ReuseUnchangedPlugins = false };

        var result = CreateBuilder(area, reparseBackend).Build(request);

        Assert.Equal(0, result.ReusedPlugins);
        Assert.Equal(2, result.ParsedPlugins);
        Assert.Equal(2, reparseBackend.OpenCount);
    }

    [Fact]
    public void IncrementalBuildRemovesPluginsNoLongerInLoadOrder()
    {
        using var area = new TestArea();
        var records = TwoPluginRecords();
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var reuseBackend = new FakeBackend(records);

        var result = CreateBuilder(area, reuseBackend).Build(Request(area, "base.esm"));

        Assert.Equal(1, result.ReusedPlugins);
        Assert.Equal(0, result.ParsedPlugins);
        Assert.Equal(0, reuseBackend.OpenCount);
        Assert.Single(new SqliteIndexRepository(area.Database).Trace("000123:Base.esm").Chain);
    }

    [Fact]
    public void CancelledIncrementalBuildPreservesPublishedDatabase()
    {
        using var area = new TestArea();
        var records = TwoPluginRecords();
        CreateBuilder(area, new FakeBackend(records)).Build(Request(area, "base.esm", "patch.esp"));
        var published = File.ReadAllBytes(area.Database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => CreateBuilder(area, new FakeBackend(records))
            .Build(Request(area, "base.esm", "patch.esp"), cancellationToken: cancellation.Token));

        Assert.Equal(published, File.ReadAllBytes(area.Database));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(area.Database)!, "*.staged"));
    }

    private static SqliteIndexBuilder CreateBuilder(TestArea area, IPluginBackend backend)
    {
        var guard = new ReadOnlySourceGuard([area.Source], area.Workspace);
        return new SqliteIndexBuilder(
            new WorkspaceFileSystem(guard),
            backend,
            new PluginEncodingClassifier());
    }

    private static IndexBuildRequest Request(TestArea area, params string[] names)
    {
        var plugins = names.Select((name, index) => new IndexPluginInput
        {
            LoadOrderIndex = index,
            Name = name,
            PhysicalPath = Path.Combine(area.Source, name),
            SourceMod = index == 0 ? "Game Data" : "Patch Mod",
            EffectivePriority = index,
            FileLength = 10,
            LastWriteUtc = DateTime.UnixEpoch,
        }).ToArray();
        return new IndexBuildRequest
        {
            DestinationPath = area.Database,
            PreviousDatabasePath = area.Database,
            Mode = GameMode.TaleOfTwoWastelands,
            Mo2Root = area.Source,
            ProfileName = "Test",
            LoadOrderFingerprint = "ABC",
            SourceLanguage = "en",
            TargetLanguage = "ru",
            Plugins = plugins,
            PhysicalProviders = [],
        };
    }

    private static RecordOccurrence Record(
        string formKey,
        string editorId,
        string text,
        string originPlugin = "Base.esm",
        string recordType = "Activator",
        string category = "display-name") => new()
        {
            FormKey = formKey,
            OriginPlugin = originPlugin,
            RecordType = recordType,
            EditorId = editorId,
            IsDeleted = false,
            IsCompressed = false,
            Strings =
        [
            new RecordStringOccurrence
            {
                SemanticPath = "Name",
                Category = category,
                Text = text,
                Language = text.Length == 0
                    ? TextLanguageKind.Empty
                    : text.Any(character => character is >= '\u0400' and <= '\u04FF')
                        ? TextLanguageKind.Target
                        : TextLanguageKind.Source,
                EncodingEvidence = text.Length == 0
                    ? StringEncodingEvidence.None
                    : text.Any(character => character is >= '\u0400' and <= '\u04FF')
                        ? StringEncodingEvidence.UnicodeTarget
                        : StringEncodingEvidence.Ascii,
                Ambiguous = false,
            },
        ],
        };

    private static RecordOccurrence EmptyRecord(
        string formKey,
        string recordType,
        RecordParseStatus status,
        params string[] warnings) => new()
        {
            FormKey = formKey,
            OriginPlugin = "Base.esm",
            RecordType = recordType,
            EditorId = null,
            IsDeleted = false,
            IsCompressed = false,
            ParseStatus = status,
            ParseWarnings = warnings,
            Strings = [],
        };

    private static IReadOnlyDictionary<string, IReadOnlyList<RecordOccurrence>> TwoPluginRecords() =>
        new Dictionary<string, IReadOnlyList<RecordOccurrence>>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.esm"] = [Record("000123:Base.esm", "Old Russian", "Старый русский текст")],
            ["patch.esp"] = [Record("000123:Base.esm", "New English", "New English text")],
        };

    private sealed class FakeBackend(
        IReadOnlyDictionary<string, IReadOnlyList<RecordOccurrence>> records,
        string name = "fake-read-only") : IPluginBackend
    {
        public string Name => name;
        public int OpenCount { get; private set; }

        public IPluginReadSession Open(PluginOpenRequest request)
        {
            OpenCount++;
            var name = Path.GetFileName(request.Path);
            if (!records.TryGetValue(name, out var pluginRecords))
            {
                throw new InvalidDataException($"Fixture plugin {name} is configured to fail.");
            }

            return new Session(request, name, pluginRecords);
        }

        private sealed class Session(
            PluginOpenRequest request,
            string name,
            IReadOnlyList<RecordOccurrence> records) : IPluginReadSession
        {
            public PluginMetadata Metadata { get; } = new()
            {
                PluginName = name,
                PhysicalPath = request.Path,
                Mode = request.Mode,
                LoadOrderIndex = request.LoadOrderIndex,
                SourceMod = request.SourceMod,
                Masters = [],
            };

            public IEnumerable<RecordOccurrence> EnumerateMajorRecords(CancellationToken cancellationToken = default) => records;

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestArea : IDisposable
    {
        public TestArea()
        {
            Root = Path.Combine(
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".falloutloc", "fixtures", "production-tests")),
                Guid.NewGuid().ToString("N"));
            Source = Path.Combine(Root, "source");
            Workspace = Path.Combine(Root, "workspace");
            Database = Path.Combine(Workspace, "index", "falloutloc.sqlite");
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        }

        public string Root { get; }
        public string Source { get; }
        public string Workspace { get; }
        public string Database { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
