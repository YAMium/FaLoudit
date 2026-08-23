using System.Text;
using System.Text.RegularExpressions;
using FalloutLoc.Analysis.Models;
using FalloutLoc.Backends.Models;
using FalloutLoc.Index;
using FalloutLoc.Index.Models;

namespace FalloutLoc.Analysis;

public sealed partial class LocalizationDiagnosticService(IIndexQuery index)
{
    public LocalizationAnalysisResult Analyze(string query, int maxCandidates = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxCandidates is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates), "Candidate limit must be between 1 and 20.");
        }

        const int searchLimit = 500;
        var page = index.SearchText(new IndexedTextSearchRequest
        {
            Query = query,
            Mode = IndexedTextSearchMode.Exact,
            Limit = searchLimit,
        });
        if (page.Items.Count == 0)
        {
            page = index.SearchText(new IndexedTextSearchRequest
            {
                Query = query,
                Mode = IndexedTextSearchMode.Exact,
                IgnoreCase = true,
                Limit = searchLimit,
            });
        }

        if (page.Items.Count == 0)
        {
            page = index.SearchText(new IndexedTextSearchRequest
            {
                Query = query,
                Mode = IndexedTextSearchMode.Contains,
                IgnoreCase = true,
                Limit = searchLimit,
            });
        }

        var status = index.GetStatus();
        if (page.Items.Count == 0)
        {
            return new LocalizationAnalysisResult
            {
                Query = query,
                Status = LocalizationAnalysisStatus.NoMatches,
                Confidence = DiagnosticConfidence.Low,
                DistinctCandidateRecords = 0,
                SearchTruncated = false,
                IndexHasParseFailures = status.FailedPlugins > 0,
                Candidates = [],
                Explanation = "No indexed localized string matches the supplied text.",
            };
        }

        var ranked = page.Items
            .GroupBy(match => match.FormKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(match => MatchQuality(query, match.Text))
                .ThenByDescending(match => match.IsWinningOverride)
                .ThenByDescending(match => match.LoadOrderIndex)
                .ThenBy(match => match.SemanticPath, StringComparer.Ordinal)
                .First())
            .Select(match => new RankedMatch(match, MatchQuality(query, match.Text)))
            .OrderBy(item => item.Quality)
            .ThenByDescending(item => item.Match.IsWinningOverride)
            .ThenBy(item => item.Match.FormKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var first = ranked[0];
        var equivalentBestCount = ranked.Count(item =>
            item.Quality == first.Quality
            && item.Match.IsWinningOverride == first.Match.IsWinningOverride);
        var ambiguous = page.HasMore || equivalentBestCount > 1;
        var candidates = ranked.Take(maxCandidates)
            .Select((item, candidateIndex) => new LocalizationAnalysisCandidate
            {
                Rank = candidateIndex + 1,
                MatchQuality = item.Quality,
                EquivalentBest = item.Quality == first.Quality
                    && item.Match.IsWinningOverride == first.Match.IsWinningOverride,
                MatchedText = item.Match.Text ?? string.Empty,
                MatchedSemanticPath = item.Match.SemanticPath,
                MatchedCategory = item.Match.Category,
                MatchedPlugin = item.Match.PluginName,
                MatchedWinningOverride = item.Match.IsWinningOverride,
                Diagnostic = Explain(item.Match.FormKey),
            })
            .ToArray();
        var confidence = ambiguous
            ? DiagnosticConfidence.Ambiguous
            : first.Quality switch
            {
                LocalizationMatchQuality.Exact when first.Match.IsWinningOverride => DiagnosticConfidence.High,
                LocalizationMatchQuality.Exact or LocalizationMatchQuality.CaseInsensitiveExact => DiagnosticConfidence.Medium,
                _ => DiagnosticConfidence.Low,
            };
        return new LocalizationAnalysisResult
        {
            Query = query,
            Status = ambiguous ? LocalizationAnalysisStatus.Ambiguous : LocalizationAnalysisStatus.Resolved,
            Confidence = confidence,
            SelectedFormKey = ambiguous ? null : first.Match.FormKey,
            DistinctCandidateRecords = ranked.Length,
            SearchTruncated = page.HasMore,
            IndexHasParseFailures = status.FailedPlugins > 0,
            Candidates = candidates,
            Explanation = ambiguous
                ? page.HasMore
                    ? "The bounded search was truncated, so a unique record cannot be selected safely."
                    : $"{equivalentBestCount} records have equivalent best text matches; context is required."
                : $"A unique {first.Quality} match identifies {first.Match.FormKey}.",
        };
    }

    public RecordDiagnostic Explain(string formKey)
    {
        var trace = index.Trace(formKey);
        var status = index.GetStatus();
        if (trace.Chain.Count == 0)
        {
            return new RecordDiagnostic
            {
                FormKey = formKey,
                Status = LocalizationDiagnosticStatus.NoRecord,
                Confidence = DiagnosticConfidence.High,
                WinningPluginProviders = [],
                Fields = [],
                IndexHasParseFailures = status.FailedPlugins > 0,
                Explanation = "The complete active index contains no occurrence of this FormKey.",
            };
        }

        var winner = trace.Chain[^1];
        var paths = trace.Chain.SelectMany(occurrence => occurrence.Strings)
            .Select(field => field.SemanticPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var fields = paths.Select(path => AnalyzeField(trace.Chain, path)).ToArray();
        var overall = SelectOverallStatus(winner, fields);
        var confidence = SelectOverallConfidence(overall, fields);
        return new RecordDiagnostic
        {
            FormKey = trace.FormKey,
            RecordType = winner.RecordType,
            EditorId = winner.EditorId,
            Status = overall,
            Confidence = confidence,
            WinningPlugin = winner.PluginName,
            WinningSourceMod = winner.SourceMod,
            WinningPhysicalPath = winner.PhysicalPath,
            WinningPluginProviders = index.GetPhysicalProviders(winner.PluginName),
            Fields = fields,
            IndexHasParseFailures = status.FailedPlugins > 0,
            Explanation = ExplainOverall(overall, fields, winner.PluginName),
        };
    }

    public RegressionReport FindRegressions(string? winningPlugin = null, int limit = 100)
        => FindRegressions(new DiagnosticReportRequest { WinningPlugin = winningPlugin, Limit = limit });

    public RegressionReport FindRegressions(DiagnosticReportRequest request)
    {
        ValidateReportRequest(request);
        var status = index.GetStatus();
        var page = index.FindDiagnosticCandidateFormKeys(ToIndexRequest(request, IndexedDiagnosticKind.Regressions));
        var records = page.Items.Select(Explain)
            .Select(record => FilterRecord(record, request, IsRegression))
            .Where(record => record is not null)
            .Cast<RecordDiagnostic>()
            .OrderBy(record => record.WinningPlugin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.FormKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new RegressionReport
        {
            WinningPluginFilter = request.WinningPlugin,
            SourceModFilter = request.SourceMod,
            RecordTypeFilter = request.RecordType,
            CategoryFilter = request.Category,
            MinimumConfidence = request.MinimumConfidence,
            ExclusionCount = request.ExcludedTexts.Count,
            CandidateRecords = page.Items.Count,
            Findings = records.Sum(record => record.Fields.Count(IsRegression)),
            Records = records,
            IndexHasParseFailures = status.FailedPlugins > 0,
            Limit = page.Limit,
            HasMore = page.HasMore,
            NextCursor = page.NextCursor,
        };
    }

    public UntranslatedReport FindUntranslated(string? winningPlugin = null, int limit = 100)
        => FindUntranslated(new DiagnosticReportRequest { WinningPlugin = winningPlugin, Limit = limit });

    public UntranslatedReport FindUntranslated(DiagnosticReportRequest request)
    {
        ValidateReportRequest(request);
        var status = index.GetStatus();
        var page = index.FindDiagnosticCandidateFormKeys(ToIndexRequest(request, IndexedDiagnosticKind.Untranslated));
        var records = page.Items.Select(Explain)
            .Select(record => FilterRecord(record, request, field => IsUntranslatedReviewCandidate(record, field)))
            .Where(record => record is not null)
            .Cast<RecordDiagnostic>()
            .OrderBy(record => record.WinningPlugin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.FormKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new UntranslatedReport
        {
            WinningPluginFilter = request.WinningPlugin,
            SourceModFilter = request.SourceMod,
            RecordTypeFilter = request.RecordType,
            CategoryFilter = request.Category,
            MinimumConfidence = request.MinimumConfidence,
            ExclusionCount = request.ExcludedTexts.Count,
            CandidateRecords = records.Length,
            CandidateFields = records.Sum(record => record.Fields.Count(field =>
                IsUntranslatedReviewCandidate(record, field))),
            Confidence = DiagnosticConfidence.Low,
            Records = records,
            IndexHasParseFailures = status.FailedPlugins > 0,
            Caveat = "Latin-only winning text without an earlier active Russian value is only a review candidate; " +
                "names, abbreviations, technical tokens, and intentional English may be valid.",
            Limit = page.Limit,
            HasMore = page.HasMore,
            NextCursor = page.NextCursor,
        };
    }

    private static IndexedDiagnosticCandidateRequest ToIndexRequest(
        DiagnosticReportRequest request,
        IndexedDiagnosticKind kind) => new()
        {
            Kind = kind,
            WinningPlugin = request.WinningPlugin,
            SourceMod = request.SourceMod,
            RecordType = request.RecordType,
            Category = request.Category,
            Limit = request.Limit,
            Cursor = request.Cursor,
        };

    private static RecordDiagnostic? FilterRecord(
        RecordDiagnostic record,
        DiagnosticReportRequest request,
        Func<FieldDiagnostic, bool> predicate)
    {
        if ((!string.IsNullOrWhiteSpace(request.RecordType)
             && !string.Equals(record.RecordType, request.RecordType, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.SourceMod)
                && !string.Equals(record.WinningSourceMod, request.SourceMod, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var fields = record.Fields.Where(field => predicate(field)
                && (string.IsNullOrWhiteSpace(request.Category)
                    || field.Category.Equals(request.Category, StringComparison.OrdinalIgnoreCase))
                && MeetsThreshold(field.Confidence, request.MinimumConfidence)
                && (field.Winner.Text is null || !request.ExcludedTexts.Contains(field.Winner.Text)))
            .GroupBy(field => $"{field.SemanticPath}\0{field.Winner.Text}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return fields.Length == 0 ? null : record with { Fields = fields };
    }

    private static bool MeetsThreshold(DiagnosticConfidence confidence, ReportConfidenceThreshold threshold) =>
        threshold switch
        {
            ReportConfidenceThreshold.High => confidence == DiagnosticConfidence.High,
            ReportConfidenceThreshold.Medium => confidence is DiagnosticConfidence.High or DiagnosticConfidence.Medium,
            ReportConfidenceThreshold.Low => confidence is not DiagnosticConfidence.Ambiguous,
            ReportConfidenceThreshold.Any => true,
            _ => throw new ArgumentOutOfRangeException(nameof(threshold), threshold, null),
        };

    private static void ValidateReportRequest(DiagnosticReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Limit must be between 1 and 10000.");
        }
    }

    public static bool IsUntranslatedReviewCandidate(RecordDiagnostic record, FieldDiagnostic field)
    {
        if (field.Status != LocalizationDiagnosticStatus.EnglishWithoutActiveRussian
            || string.IsNullOrWhiteSpace(field.Winner.Text))
        {
            return false;
        }

        var text = field.Winner.Text;
        if (TechnicalAssetPathRegex().IsMatch(text))
        {
            return false;
        }

        if (record.RecordType == "GameSettingString" && record.EditorId is not null
            && TechnicalGameSettingRegex().IsMatch(record.EditorId))
        {
            return false;
        }

        return true;
    }

    private static FieldDiagnostic AnalyzeField(IReadOnlyList<IndexedOverride> chain, string path)
    {
        var winner = chain[^1];
        var winningField = winner.Strings.LastOrDefault(field => field.SemanticPath == path);
        var previousRussian = chain.Take(chain.Count - 1)
            .Select(occurrence => (Occurrence: occurrence,
                Field: occurrence.Strings.LastOrDefault(field => field.SemanticPath == path)))
            .LastOrDefault(item => item.Field?.Language == TextLanguageKind.Russian);
        var category = winningField?.Category ?? previousRussian.Field?.Category ?? "unknown";
        var synthesizedWinner = winningField ?? new IndexedTraceString
        {
            SemanticPath = path,
            Category = category,
            Text = null,
            Language = TextLanguageKind.Empty,
            EncodingEvidence = StringEncodingEvidence.None,
            Ambiguous = false,
        };
        var structuralChange = HasOrdinalStructuralChange(chain, path);
        var winnerOccurrence = ToDiagnosticOccurrence(winner, synthesizedWinner);
        var earlierOccurrence = previousRussian.Field is null
            ? null
            : ToDiagnosticOccurrence(previousRussian.Occurrence!, previousRussian.Field);

        var (diagnosticStatus, confidence, explanation) = Classify(
            winner,
            synthesizedWinner,
            earlierOccurrence,
            structuralChange);
        return new FieldDiagnostic
        {
            SemanticPath = path,
            Category = category,
            Status = diagnosticStatus,
            Confidence = confidence,
            EarlierRussian = earlierOccurrence,
            Winner = winnerOccurrence,
            StructuralChange = structuralChange,
            Explanation = explanation,
        };
    }

    private static (LocalizationDiagnosticStatus Status, DiagnosticConfidence Confidence, string Explanation) Classify(
        IndexedOverride winningRecord,
        IndexedTraceString winner,
        DiagnosticStringOccurrence? earlierRussian,
        bool structuralChange)
    {
        if (winningRecord.IsDeleted)
        {
            return (LocalizationDiagnosticStatus.DeletedWinner, DiagnosticConfidence.High,
                "The winning record override is deleted; its strings are not active game values.");
        }

        if (winner.Ambiguous || winner.EncodingEvidence is
            StringEncodingEvidence.SingleByteAmbiguous or StringEncodingEvidence.UnrecoverableUnicode)
        {
            return (LocalizationDiagnosticStatus.Ambiguous, DiagnosticConfidence.Ambiguous,
                "The winning value has ambiguous or unrecoverable encoding evidence.");
        }

        if (earlierRussian is not null && winner.Language != TextLanguageKind.Russian)
        {
            if (structuralChange)
            {
                return (LocalizationDiagnosticStatus.Ambiguous, DiagnosticConfidence.Ambiguous,
                    "An earlier Russian value exists, but this ordinal list changed shape; automatic field pairing is unsafe.");
            }

            return winner.Language switch
            {
                TextLanguageKind.English => (LocalizationDiagnosticStatus.TranslationRegression, DiagnosticConfidence.High,
                    $"{winningRecord.PluginName} replaced the earlier Russian value from {earlierRussian.PluginName} with English."),
                TextLanguageKind.Empty => (LocalizationDiagnosticStatus.ClearedTranslation, DiagnosticConfidence.Medium,
                    $"{winningRecord.PluginName} cleared or omitted the earlier Russian value from {earlierRussian.PluginName}."),
                _ => (LocalizationDiagnosticStatus.NonRussianRegression, DiagnosticConfidence.Low,
                    $"{winningRecord.PluginName} replaced the earlier Russian value with a value that is neither confidently Russian nor English."),
            };
        }

        return winner.Language switch
        {
            TextLanguageKind.Russian => (LocalizationDiagnosticStatus.LocalizedRussian, DiagnosticConfidence.High,
                "The winning value contains Cyrillic and is classified as Russian."),
            TextLanguageKind.English => (LocalizationDiagnosticStatus.EnglishWithoutActiveRussian, DiagnosticConfidence.Low,
                "The winner looks English, but no earlier active Russian value exists for this field; Latin text alone is not proof of a defect."),
            TextLanguageKind.Empty => (LocalizationDiagnosticStatus.EmptyWinner, DiagnosticConfidence.Low,
                "The winning field is empty and no earlier active Russian value was found."),
            _ => (LocalizationDiagnosticStatus.Neutral, DiagnosticConfidence.Low,
                "The winning value is neutral or cannot be classified as Russian or English."),
        };
    }

    private static DiagnosticStringOccurrence ToDiagnosticOccurrence(
        IndexedOverride occurrence,
        IndexedTraceString field) => new()
        {
            PluginName = occurrence.PluginName,
            LoadOrderIndex = occurrence.LoadOrderIndex,
            PhysicalPath = occurrence.PhysicalPath,
            SourceMod = occurrence.SourceMod,
            EffectivePriority = occurrence.EffectivePriority,
            SemanticPath = field.SemanticPath,
            Category = field.Category,
            Text = field.Text,
            Language = field.Language,
            EncodingEvidence = field.EncodingEvidence,
            Ambiguous = field.Ambiguous,
        };

    private static bool HasOrdinalStructuralChange(IReadOnlyList<IndexedOverride> chain, string path)
    {
        var group = OrdinalGroup(path);
        if (group is null)
        {
            return false;
        }

        var shapes = chain.Select(occurrence => occurrence.Strings
                .Select(field => OrdinalIdentity(field.SemanticPath, group))
                .Where(identity => identity is not null)
                .Distinct(StringComparer.Ordinal)
                .Count())
            .Distinct()
            .Count();
        return shapes > 1;
    }

    private static string? OrdinalGroup(string path)
    {
        if (path.StartsWith("MenuItems[", StringComparison.Ordinal))
        {
            return "MenuItems";
        }

        if (path.StartsWith("MenuButtons[", StringComparison.Ordinal))
        {
            return "MenuButtons";
        }

        var stage = QuestStageRegex().Match(path);
        return stage.Success ? $"QuestStage:{stage.Groups[1].Value}" : null;
    }

    private static string? OrdinalIdentity(string path, string group)
    {
        var pattern = group switch
        {
            "MenuItems" => MenuItemRegex(),
            "MenuButtons" => MenuButtonRegex(),
            _ when group.StartsWith("QuestStage:", StringComparison.Ordinal) => QuestLogRegex(),
            _ => null,
        };
        if (pattern is null)
        {
            return null;
        }

        var match = pattern.Match(path);
        if (!match.Success)
        {
            return null;
        }

        if (group.StartsWith("QuestStage:", StringComparison.Ordinal)
            && match.Groups[1].Value != group["QuestStage:".Length..])
        {
            return null;
        }

        return group.StartsWith("QuestStage:", StringComparison.Ordinal)
            ? match.Groups[2].Value
            : match.Groups[1].Value;
    }

    private static LocalizationDiagnosticStatus SelectOverallStatus(
        IndexedOverride winner,
        IReadOnlyList<FieldDiagnostic> fields)
    {
        if (winner.IsDeleted)
        {
            return LocalizationDiagnosticStatus.DeletedWinner;
        }

        foreach (var candidate in new[]
                 {
                     LocalizationDiagnosticStatus.TranslationRegression,
                     LocalizationDiagnosticStatus.ClearedTranslation,
                     LocalizationDiagnosticStatus.NonRussianRegression,
                     LocalizationDiagnosticStatus.Ambiguous,
                     LocalizationDiagnosticStatus.LocalizedRussian,
                     LocalizationDiagnosticStatus.EnglishWithoutActiveRussian,
                     LocalizationDiagnosticStatus.EmptyWinner,
                 })
        {
            if (fields.Any(field => field.Status == candidate))
            {
                return candidate;
            }
        }

        return LocalizationDiagnosticStatus.Neutral;
    }

    private static DiagnosticConfidence SelectOverallConfidence(
        LocalizationDiagnosticStatus status,
        IReadOnlyList<FieldDiagnostic> fields) =>
        fields.Where(field => field.Status == status).Select(field => (DiagnosticConfidence?)field.Confidence).FirstOrDefault()
        ?? DiagnosticConfidence.Low;

    private static string ExplainOverall(
        LocalizationDiagnosticStatus status,
        IReadOnlyList<FieldDiagnostic> fields,
        string winningPlugin)
    {
        var count = fields.Count(field => field.Status == status);
        return status switch
        {
            LocalizationDiagnosticStatus.TranslationRegression =>
                $"The winning record in {winningPlugin} contains {count} high-confidence Russian-to-English regression(s).",
            LocalizationDiagnosticStatus.ClearedTranslation =>
                $"The winning record in {winningPlugin} clears {count} earlier Russian field(s).",
            LocalizationDiagnosticStatus.Ambiguous =>
                "The record contains structural or encoding ambiguity and requires manual/xEdit review.",
            LocalizationDiagnosticStatus.LocalizedRussian =>
                "The winning record retains Russian localized text.",
            LocalizationDiagnosticStatus.DeletedWinner =>
                $"The winning override in {winningPlugin} deletes the record.",
            _ => $"The winning record is supplied by {winningPlugin}; no high-confidence RU-to-EN regression was proven.",
        };
    }

    private static bool IsRegression(FieldDiagnostic field) =>
        field.Status is LocalizationDiagnosticStatus.TranslationRegression or
            LocalizationDiagnosticStatus.ClearedTranslation or
            LocalizationDiagnosticStatus.NonRussianRegression
        || field.Status == LocalizationDiagnosticStatus.Ambiguous && field.EarlierRussian is not null;

    private static LocalizationMatchQuality MatchQuality(string query, string? text)
    {
        if (text is null)
        {
            return LocalizationMatchQuality.Contains;
        }

        if (string.Equals(text, query, StringComparison.Ordinal))
        {
            return LocalizationMatchQuality.Exact;
        }

        var normalizedText = text.Normalize(NormalizationForm.FormKC);
        var normalizedQuery = query.Normalize(NormalizationForm.FormKC);
        if (string.Equals(normalizedText, normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationMatchQuality.CaseInsensitiveExact;
        }

        return normalizedText.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)
            ? LocalizationMatchQuality.Prefix
            : LocalizationMatchQuality.Contains;
    }

    private sealed record RankedMatch(IndexedStringMatch Match, LocalizationMatchQuality Quality);

    [GeneratedRegex(@"^Stages\[index=([^\]]+)\]\.LogEntries\[")]
    private static partial Regex QuestStageRegex();

    [GeneratedRegex(@"^MenuItems\[(\d+)\]\.")]
    private static partial Regex MenuItemRegex();

    [GeneratedRegex(@"^MenuButtons\[(\d+)\]\.")]
    private static partial Regex MenuButtonRegex();

    [GeneratedRegex(@"^Stages\[index=([^\]]+)\]\.LogEntries\[(\d+)\]\.")]
    private static partial Regex QuestLogRegex();

    [GeneratedRegex(@"[\\/].*\.(?:nif|dds|tga|png|jpg|wav|mp3|ogg|bik|xml|txt)$", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalAssetPathRegex();

    [GeneratedRegex(@"(?:Image|Model|Mesh|Texture|Icon|Sound|Path|Filename|File)$", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalGameSettingRegex();
}
