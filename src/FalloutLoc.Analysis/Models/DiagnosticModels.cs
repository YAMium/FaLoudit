using FalloutLoc.Backends.Models;
using FalloutLoc.Index.Models;
using System.Text.Json.Serialization;

namespace FalloutLoc.Analysis.Models;

public enum LocalizationDiagnosticStatus
{
    NoRecord,
    LocalizedTarget,
    TranslationRegression,
    ClearedTranslation,
    NonTargetRegression,
    SourceWithoutActiveTarget,
    EmptyWinner,
    DeletedWinner,
    Neutral,
    Ambiguous,

    LocalizedRussian = LocalizedTarget,
    NonRussianRegression = NonTargetRegression,
    EnglishWithoutActiveRussian = SourceWithoutActiveTarget,
}

public enum DiagnosticConfidence
{
    High,
    Medium,
    Low,
    Ambiguous,
}

public sealed record DiagnosticStringOccurrence
{
    public required string PluginName { get; init; }
    public required int LoadOrderIndex { get; init; }
    public required string PhysicalPath { get; init; }
    public required string SourceMod { get; init; }
    public required long EffectivePriority { get; init; }
    public required string SemanticPath { get; init; }
    public required string Category { get; init; }
    public string? Text { get; init; }
    public required TextLanguageKind Language { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
}

public sealed record FieldDiagnostic
{
    public required string SemanticPath { get; init; }
    public required string Category { get; init; }
    public required LocalizationDiagnosticStatus Status { get; init; }
    public required DiagnosticConfidence Confidence { get; init; }
    public DiagnosticStringOccurrence? EarlierTarget { get; init; }
    [JsonIgnore]
    public DiagnosticStringOccurrence? EarlierRussian => EarlierTarget;
    public required DiagnosticStringOccurrence Winner { get; init; }
    public required bool StructuralChange { get; init; }
    public required string Explanation { get; init; }
}

public sealed record RecordDiagnostic
{
    public required string FormKey { get; init; }
    public string? RecordType { get; init; }
    public string? EditorId { get; init; }
    public required LocalizationDiagnosticStatus Status { get; init; }
    public required DiagnosticConfidence Confidence { get; init; }
    public string? WinningPlugin { get; init; }
    public string? WinningSourceMod { get; init; }
    public string? WinningPhysicalPath { get; init; }
    public required IReadOnlyList<IndexedPhysicalProvider> WinningPluginProviders { get; init; }
    public required IReadOnlyList<FieldDiagnostic> Fields { get; init; }
    public required bool IndexHasParseFailures { get; init; }
    public required string Explanation { get; init; }
}

public sealed record RegressionReport
{
    public string SourceLanguage { get; init; } = "source";
    public string TargetLanguage { get; init; } = "target";
    public string? WinningPluginFilter { get; init; }
    public string? SourceModFilter { get; init; }
    public string? RecordTypeFilter { get; init; }
    public string? CategoryFilter { get; init; }
    public ReportConfidenceThreshold MinimumConfidence { get; init; } = ReportConfidenceThreshold.Low;
    public int ExclusionCount { get; init; }
    public required int CandidateRecords { get; init; }
    public required int Findings { get; init; }
    public required IReadOnlyList<RecordDiagnostic> Records { get; init; }
    public required bool IndexHasParseFailures { get; init; }
    public int Limit { get; init; }
    public bool HasMore { get; init; }
    public string? NextCursor { get; init; }
}

public sealed record UntranslatedReport
{
    public string SourceLanguage { get; init; } = "source";
    public string TargetLanguage { get; init; } = "target";
    public string? WinningPluginFilter { get; init; }
    public string? SourceModFilter { get; init; }
    public string? RecordTypeFilter { get; init; }
    public string? CategoryFilter { get; init; }
    public ReportConfidenceThreshold MinimumConfidence { get; init; } = ReportConfidenceThreshold.Low;
    public int ExclusionCount { get; init; }
    public required int CandidateRecords { get; init; }
    public required int CandidateFields { get; init; }
    public required DiagnosticConfidence Confidence { get; init; }
    public required IReadOnlyList<RecordDiagnostic> Records { get; init; }
    public required bool IndexHasParseFailures { get; init; }
    public required string Caveat { get; init; }
    public int Limit { get; init; }
    public bool HasMore { get; init; }
    public string? NextCursor { get; init; }
}

public enum ReportConfidenceThreshold
{
    High,
    Medium,
    Low,
    Any,
}

public sealed record DiagnosticReportRequest
{
    public string? WinningPlugin { get; init; }
    public string? SourceMod { get; init; }
    public string? RecordType { get; init; }
    public string? Category { get; init; }
    public ReportConfidenceThreshold MinimumConfidence { get; init; } = ReportConfidenceThreshold.Low;
    public IReadOnlySet<string> ExcludedTexts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public int Limit { get; init; } = 100;
    public string? Cursor { get; init; }
}

public enum LocalizationAnalysisStatus
{
    NoMatches,
    Resolved,
    Ambiguous,
}

public enum LocalizationMatchQuality
{
    Exact,
    CaseInsensitiveExact,
    Prefix,
    Contains,
}

public sealed record LocalizationAnalysisCandidate
{
    public required int Rank { get; init; }
    public required LocalizationMatchQuality MatchQuality { get; init; }
    public required bool EquivalentBest { get; init; }
    public required string MatchedText { get; init; }
    public required string MatchedSemanticPath { get; init; }
    public required string MatchedCategory { get; init; }
    public required string MatchedPlugin { get; init; }
    public required bool MatchedWinningOverride { get; init; }
    public required RecordDiagnostic Diagnostic { get; init; }
}

public sealed record LocalizationAnalysisResult
{
    public required string Query { get; init; }
    public required LocalizationAnalysisStatus Status { get; init; }
    public required DiagnosticConfidence Confidence { get; init; }
    public string? SelectedFormKey { get; init; }
    public required int DistinctCandidateRecords { get; init; }
    public required bool SearchTruncated { get; init; }
    public required bool IndexHasParseFailures { get; init; }
    public required IReadOnlyList<LocalizationAnalysisCandidate> Candidates { get; init; }
    public required string Explanation { get; init; }
}
