using FalloutLoc.Analysis.Models;
using FalloutLoc.Backends.Models;
using FalloutLoc.Core.Configuration;
using FalloutLoc.Index;
using FalloutLoc.Index.Models;

namespace FalloutLoc.Analysis.Tests;

public sealed class LocalizationDiagnosticServiceTests
{
    [Fact]
    public void DetectsHighConfidenceEnglishRussianEnglishRegression()
    {
        var service = Service(
            Override("Base.esm", 0, Field("Name", "Tactical Helmet", TextLanguageKind.English)),
            Override("Russian.esp", 1, Field("Name", "Тактический шлем", TextLanguageKind.Russian)),
            Override("Patch.esp", 2, Field("Name", "Tactical Helmet", TextLanguageKind.English)));

        var result = service.Explain(FormKey);

        Assert.Equal(LocalizationDiagnosticStatus.TranslationRegression, result.Status);
        Assert.Equal(DiagnosticConfidence.High, result.Confidence);
        var field = Assert.Single(result.Fields);
        Assert.Equal("Russian.esp", field.EarlierRussian?.PluginName);
        Assert.Equal("Patch.esp", field.Winner.PluginName);
        Assert.Equal("Patch Mod", result.WinningSourceMod);
    }

    [Fact]
    public void DetectsClearedEarlierRussianValue()
    {
        var service = Service(
            Override("Russian.esp", 0, Field("Name", "Перевод", TextLanguageKind.Russian)),
            Override("Patch.esp", 1));

        var result = service.Explain(FormKey);

        Assert.Equal(LocalizationDiagnosticStatus.ClearedTranslation, result.Status);
        Assert.Equal(DiagnosticConfidence.Medium, result.Confidence);
        Assert.Null(Assert.Single(result.Fields).Winner.Text);
    }

    [Fact]
    public void DetectsExplicitPolishTargetRegression()
    {
        var query = new FakeIndexQuery(
            [Override("Base.esm", 0, Field("Name", "Combat armor", TextLanguageKind.Source)),
             Override("Polish.esp", 1, Field("Name", "Pancerz żołnierza", TextLanguageKind.Target)),
             Override("Patch.esp", 2, Field("Name", "Combat armor", TextLanguageKind.Source))],
            [FormKey],
            sourceLanguage: "en",
            targetLanguage: "pl");

        var field = Assert.Single(new LocalizationDiagnosticService(query).Explain(FormKey).Fields);

        Assert.Equal(LocalizationDiagnosticStatus.TranslationRegression, field.Status);
        Assert.Equal(DiagnosticConfidence.High, field.Confidence);
        Assert.Equal("Polish.esp", field.EarlierTarget?.PluginName);
    }

    [Fact]
    public void ExactSourceReversionFindsSharedLatinTargetConservatively()
    {
        var query = new FakeIndexQuery(
            [Override("Base.esm", 0, Field("Name", "Armor", TextLanguageKind.Source)),
             Override("Polish.esp", 1, Field("Name", "Pancerz", TextLanguageKind.Source)),
             Override("Patch.esp", 2, Field("Name", "Armor", TextLanguageKind.Source))],
            [FormKey],
            sourceLanguage: "en",
            targetLanguage: "pl");

        var field = Assert.Single(new LocalizationDiagnosticService(query).Explain(FormKey).Fields);

        Assert.Equal(LocalizationDiagnosticStatus.TranslationRegression, field.Status);
        Assert.Equal(DiagnosticConfidence.Medium, field.Confidence);
        Assert.Equal("Polish.esp", field.EarlierTarget?.PluginName);
    }

    [Fact]
    public void ExactSourceReversionDoesNotTreatEmptyIntermediateValueAsTranslation()
    {
        var query = new FakeIndexQuery(
            [Override("Base.esm", 0, Field("Name", "Armor", TextLanguageKind.Source)),
             Override("Empty.esp", 1, Field("Name", "", TextLanguageKind.Empty)),
             Override("Patch.esp", 2, Field("Name", "Armor", TextLanguageKind.Source))],
            [FormKey],
            sourceLanguage: "en",
            targetLanguage: "pl");

        var field = Assert.Single(new LocalizationDiagnosticService(query).Explain(FormKey).Fields);

        Assert.Equal(LocalizationDiagnosticStatus.SourceWithoutActiveTarget, field.Status);
        Assert.Null(field.EarlierTarget);
    }

    [Fact]
    public void MarksChangedOrdinalListAsAmbiguousInsteadOfRegression()
    {
        var service = Service(
            Override("Russian.esp", 0,
                Field("MenuItems[0].ItemText", "Отчёт", TextLanguageKind.Russian)),
            Override("Patch.esp", 1,
                Field("MenuItems[0].ItemText", "Report", TextLanguageKind.English),
                Field("MenuItems[1].ItemText", "Exit", TextLanguageKind.English)));

        var result = service.Explain(FormKey);

        var field = Assert.Single(result.Fields, field => field.SemanticPath == "MenuItems[0].ItemText");
        Assert.Equal(LocalizationDiagnosticStatus.Ambiguous, field.Status);
        Assert.Equal(DiagnosticConfidence.Ambiguous, field.Confidence);
        Assert.True(field.StructuralChange);
    }

    [Fact]
    public void DoesNotCallEnglishOnlyTextAProvenRegression()
    {
        var result = Service(Override("Base.esm", 0,
            Field("Name", "Advanced Targeting System", TextLanguageKind.English))).Explain(FormKey);

        Assert.Equal(LocalizationDiagnosticStatus.EnglishWithoutActiveRussian, result.Status);
        Assert.Equal(DiagnosticConfidence.Low, result.Confidence);
    }

    [Fact]
    public void EncodingAmbiguityOverridesLanguageGuess()
    {
        var ambiguous = Field("Name", "Smart ’ quote", TextLanguageKind.English) with
        {
            EncodingEvidence = StringEncodingEvidence.SingleByteAmbiguous,
            Ambiguous = true,
        };

        var result = Service(Override("Patch.esp", 0, ambiguous)).Explain(FormKey);

        Assert.Equal(LocalizationDiagnosticStatus.Ambiguous, result.Status);
        Assert.Equal(DiagnosticConfidence.Ambiguous, result.Confidence);
    }

    [Fact]
    public void MassReportReturnsOnlyCandidateDiagnostics()
    {
        var query = new FakeIndexQuery(
            [Override("Russian.esp", 0, Field("Name", "Перевод", TextLanguageKind.Russian)),
             Override("Patch.esp", 1, Field("Name", "Translation", TextLanguageKind.English))],
            [FormKey]);

        var report = new LocalizationDiagnosticService(query).FindRegressions("Patch.esp", 10);

        Assert.Equal(1, report.CandidateRecords);
        Assert.Equal(1, report.Findings);
        Assert.Single(report.Records);
        Assert.Equal("Patch.esp", query.RequestedPlugin);
    }

    [Fact]
    public void UntranslatedReportStaysLowConfidence()
    {
        var query = new FakeIndexQuery(
            [Override("Patch.esp", 0, Field("Name", "Dog", TextLanguageKind.English))],
            [FormKey]);

        var report = new LocalizationDiagnosticService(query).FindUntranslated(null, 10);

        Assert.Equal(DiagnosticConfidence.Low, report.Confidence);
        Assert.Equal(1, report.CandidateFields);
        Assert.Contains("review candidate", report.Caveat, StringComparison.Ordinal);
    }

    [Fact]
    public void UntranslatedReportExcludesTechnicalAssetPaths()
    {
        var record = Override("Base.esm", 0,
            Field("Data", @"Interface\Icons\glow.dds", TextLanguageKind.English)) with
        {
            RecordType = "GameSettingString",
            EditorId = "sKarmaImage",
        };
        var report = new LocalizationDiagnosticService(new FakeIndexQuery([record], [FormKey]))
            .FindUntranslated(null, 10);

        Assert.Empty(report.Records);
        Assert.Equal(0, report.CandidateFields);
    }

    [Fact]
    public void MarkdownReportContainsWinnerAndPreviousRussianEvidence()
    {
        var report = Service(
            Override("Russian.esp", 0, Field("Name", "Перевод", TextLanguageKind.Russian)),
            Override("Patch.esp", 1, Field("Name", "Translation", TextLanguageKind.English)))
            .FindRegressions(null, 10);

        var markdown = DiagnosticReportRenderer.RenderRegressionMarkdown(
            report,
            new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc));

        Assert.Contains("# Localization regression report", markdown, StringComparison.Ordinal);
        Assert.Contains("Russian.esp", markdown, StringComparison.Ordinal);
        Assert.Contains("Patch.esp", markdown, StringComparison.Ordinal);
        Assert.Contains("TranslationRegression", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void MassReportAppliesCategoryConfidenceExclusionsAndPagination()
    {
        var query = new FakeIndexQuery(
            [Override("Russian.esp", 0,
                 Field("Name", "Перевод", TextLanguageKind.Russian),
                 Field("Description", "Описание", TextLanguageKind.Russian) with { Category = "description" }),
             Override("Patch.esp", 1,
                 Field("Name", "Intentional English", TextLanguageKind.English),
                 Field("Description", null, TextLanguageKind.Empty) with { Category = "description" })],
            [FormKey, "000456:Base.esm"]);
        var service = new LocalizationDiagnosticService(query);

        var excluded = service.FindRegressions(new DiagnosticReportRequest
        {
            Category = "display-name",
            ExcludedTexts = new HashSet<string>(["Intentional English"], StringComparer.OrdinalIgnoreCase),
            Limit = 1,
        });
        var medium = service.FindRegressions(new DiagnosticReportRequest
        {
            Category = "description",
            MinimumConfidence = ReportConfidenceThreshold.Medium,
            Limit = 1,
        });

        Assert.Empty(excluded.Records);
        Assert.True(excluded.HasMore);
        Assert.NotNull(excluded.NextCursor);
        var field = Assert.Single(Assert.Single(medium.Records).Fields);
        Assert.Equal(DiagnosticConfidence.Medium, field.Confidence);
        Assert.Equal("description", field.Category);
    }

    [Fact]
    public void CsvHtmlAndSnapshotDiffExposeNewProblems()
    {
        var baselineReport = Service(
            Override("Base.esm", 0, Field("Name", "English", TextLanguageKind.English)))
            .FindRegressions(null, 10);
        var currentReport = Service(
            Override("Russian.esp", 0, Field("Name", "Перевод", TextLanguageKind.Russian)),
            Override("Patch.esp", 1, Field("Name", "English", TextLanguageKind.English)))
            .FindRegressions(null, 10);
        var baseline = DiagnosticSnapshotService.Create(
            "regressions", "OLD", baselineReport.Records, false, DateTime.UnixEpoch);
        var current = DiagnosticSnapshotService.Create(
            "regressions", "NEW", currentReport.Records, false, DateTime.UnixEpoch);

        var diff = DiagnosticSnapshotService.Compare(baseline, current);
        var csv = DiagnosticReportRenderer.RenderRegressionCsv(currentReport);
        var html = DiagnosticReportRenderer.RenderDiffHtml(diff, DateTime.UnixEpoch);

        Assert.Single(diff.Added);
        Assert.Empty(diff.Resolved);
        Assert.Contains("FormKey,RecordType", csv, StringComparison.Ordinal);
        Assert.Contains("TranslationRegression", csv, StringComparison.Ordinal);
        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("added", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeSelectsUniqueExactWinningRecordAndReturnsFullDiagnosis()
    {
        var query = new FakeIndexQuery(
            [Override("Base.esm", 0, Field("Name", "Tactical Helmet", TextLanguageKind.English)),
             Override("Russian.esp", 1, Field("Name", "Тактический шлем", TextLanguageKind.Russian)),
             Override("Patch.esp", 2, Field("Name", "Tactical Helmet", TextLanguageKind.English))],
            [FormKey],
            [SearchMatch(FormKey, "Tactical Helmet", isWinningOverride: true)]);

        var result = new LocalizationDiagnosticService(query).Analyze("Tactical Helmet");

        Assert.Equal(LocalizationAnalysisStatus.Resolved, result.Status);
        Assert.Equal(DiagnosticConfidence.High, result.Confidence);
        Assert.Equal(FormKey, result.SelectedFormKey);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(LocalizationMatchQuality.Exact, candidate.MatchQuality);
        Assert.Equal(LocalizationDiagnosticStatus.TranslationRegression, candidate.Diagnostic.Status);
        Assert.Equal("Patch.esp", candidate.Diagnostic.WinningPlugin);
    }

    [Fact]
    public void AnalyzeReturnsAmbiguousForEquivalentExactRecords()
    {
        const string otherFormKey = "000456:Base.esm";
        var query = new FakeIndexQuery(
            [Override("Patch.esp", 2, Field("Name", "New Vegas Strip", TextLanguageKind.English))],
            [FormKey, otherFormKey],
            [SearchMatch(FormKey, "New Vegas Strip", isWinningOverride: true),
             SearchMatch(otherFormKey, "New Vegas Strip", isWinningOverride: true)]);

        var result = new LocalizationDiagnosticService(query).Analyze("New Vegas Strip");

        Assert.Equal(LocalizationAnalysisStatus.Ambiguous, result.Status);
        Assert.Equal(DiagnosticConfidence.Ambiguous, result.Confidence);
        Assert.Null(result.SelectedFormKey);
        Assert.Equal(2, result.DistinctCandidateRecords);
        Assert.All(result.Candidates, candidate => Assert.True(candidate.EquivalentBest));
    }

    [Fact]
    public void AnalyzeReturnsNoMatchesWithoutGuessing()
    {
        var result = Service(Override("Base.esm", 0,
            Field("Name", "Known text", TextLanguageKind.English))).Analyze("Missing text");

        Assert.Equal(LocalizationAnalysisStatus.NoMatches, result.Status);
        Assert.Equal(DiagnosticConfidence.Low, result.Confidence);
        Assert.Null(result.SelectedFormKey);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void AnalyzeRanksPrefixAheadOfOrdinaryContainsMatch()
    {
        const string prefixFormKey = "000456:Base.esm";
        var query = new FakeIndexQuery(
            [Override("Patch.esp", 2, Field("Name", "Target prefix", TextLanguageKind.English))],
            [FormKey, prefixFormKey],
            [SearchMatch(FormKey, "Alpha target suffix", isWinningOverride: true),
             SearchMatch(prefixFormKey, "Target prefix", isWinningOverride: true)]);

        var result = new LocalizationDiagnosticService(query).Analyze("target");

        Assert.Equal(LocalizationAnalysisStatus.Resolved, result.Status);
        Assert.Equal(prefixFormKey, result.SelectedFormKey);
        Assert.Equal(LocalizationMatchQuality.Prefix, result.Candidates[0].MatchQuality);
        Assert.Equal(LocalizationMatchQuality.Contains, result.Candidates[1].MatchQuality);
    }

    private const string FormKey = "000123:Base.esm";

    private static LocalizationDiagnosticService Service(params IndexedOverride[] chain) =>
        new(new FakeIndexQuery(chain, [FormKey]));

    private static IndexedOverride Override(string plugin, int order, params IndexedTraceString[] fields) => new()
    {
        PluginName = plugin,
        LoadOrderIndex = order,
        PhysicalPath = $@"C:\mods\{plugin}",
        SourceMod = plugin == "Patch.esp" ? "Patch Mod" : plugin,
        EffectivePriority = order,
        RecordType = "Armor",
        EditorId = "TacticalHelmet01",
        IsDeleted = false,
        IsCompressed = false,
        IsWinner = false,
        Strings = fields,
    };

    private static IndexedTraceString Field(string path, string? text, TextLanguageKind language) => new()
    {
        SemanticPath = path,
        Category = "display-name",
        Text = text,
        Language = language,
        EncodingEvidence = language == TextLanguageKind.Russian
            ? StringEncodingEvidence.Windows1251Recovered
            : text is null ? StringEncodingEvidence.None : StringEncodingEvidence.Ascii,
        Ambiguous = false,
    };

    private static IndexedStringMatch SearchMatch(
        string formKey,
        string text,
        bool isWinningOverride) => new()
        {
            FormKey = formKey,
            RecordType = "Armor",
            EditorId = "TacticalHelmet01",
            PluginName = "Patch.esp",
            LoadOrderIndex = 2,
            PhysicalPath = @"C:\mods\Patch.esp",
            SourceMod = "Patch Mod",
            SemanticPath = "Name",
            Category = "display-name",
            Text = text,
            Language = TextLanguageKind.English,
            EncodingEvidence = StringEncodingEvidence.Ascii,
            Ambiguous = false,
            IsWinningOverride = isWinningOverride,
        };

    private sealed class FakeIndexQuery(
        IReadOnlyList<IndexedOverride> chain,
        IReadOnlyList<string> candidates,
        IReadOnlyList<IndexedStringMatch>? searchMatches = null,
        string sourceLanguage = "en",
        string targetLanguage = "ru") : IIndexQuery
    {
        public string? RequestedPlugin { get; private set; }

        public IReadOnlyList<IndexedStringMatch> Find(string query, int limit = 50) => [];

        public IndexedPage<IndexedStringMatch> SearchText(IndexedTextSearchRequest request)
        {
            var comparison = request.IgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var matches = (searchMatches ?? [])
                .Where(match => request.Mode switch
                {
                    IndexedTextSearchMode.Exact => string.Equals(match.Text, request.Query, comparison),
                    IndexedTextSearchMode.Contains => match.Text?.Contains(request.Query, comparison) == true,
                    _ => false,
                })
                .Take(request.Limit)
                .ToArray();
            return new IndexedPage<IndexedStringMatch>
            {
                Items = matches,
                Limit = request.Limit,
                HasMore = false,
            };
        }

        public IndexedPage<IndexedRecordMatch> FindByEditorId(IndexedEditorIdSearchRequest request) => new()
        {
            Items = [],
            Limit = request.Limit,
            HasMore = false,
        };

        public IndexedFormLookupResult ResolveForm(string input, int limit = 50, string? cursor = null) => new()
        {
            Input = input,
            Kind = IndexedFormLookupKind.FormKey,
            LocalFormId = "000123",
            IsAmbiguous = false,
            Matches = new IndexedPage<IndexedRecordMatch>
            {
                Items = [],
                Limit = limit,
                HasMore = false,
            },
        };

        public IndexedOverrideTrace Trace(string formKey) => new() { FormKey = formKey, Chain = chain };

        public IReadOnlyList<string> FindRegressionCandidateFormKeys(string? winningPlugin, int limit)
        {
            RequestedPlugin = winningPlugin;
            return candidates.Take(limit).ToArray();
        }

        public IReadOnlyList<string> FindUntranslatedCandidateFormKeys(string? winningPlugin, int limit) =>
            candidates.Take(limit).ToArray();

        public IndexedPage<string> FindDiagnosticCandidateFormKeys(IndexedDiagnosticCandidateRequest request)
        {
            RequestedPlugin = request.WinningPlugin;
            var items = candidates.Take(request.Limit).ToArray();
            return new IndexedPage<string>
            {
                Items = items,
                Limit = request.Limit,
                HasMore = candidates.Count > items.Length,
                NextCursor = candidates.Count > items.Length ? "fake-cursor" : null,
            };
        }

        public IReadOnlyList<IndexedPhysicalProvider> GetPhysicalProviders(string logicalPath) =>
        [
            new IndexedPhysicalProvider
            {
                LogicalPath = logicalPath,
                SourceKind = "Mod",
                SourceName = "Patch Mod",
                EffectivePriority = 2,
                PhysicalPath = @"C:\mods\Patch Mod\Patch.esp",
                IsWinner = true,
            },
        ];

        public IndexSnapshotStatus GetStatus() => new()
        {
            SchemaVersion = 1,
            CreatedUtc = DateTime.UtcNow,
            Mode = GameMode.TaleOfTwoWastelands,
            ProfileName = "Test",
            LoadOrderFingerprint = "ABC",
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            BackendName = "Fake",
            ParsedPlugins = chain.Count,
            FailedPlugins = 0,
        };

        public IndexCoverageReport GetCoverage(int issueLimit = 100) =>
            throw new NotSupportedException();
    }
}
