using System.Text.Json.Serialization;

namespace FalloutLoc.Analysis.Models;

public sealed record DiagnosticFindingSnapshot
{
    public required string Identity { get; init; }
    public required string FormKey { get; init; }
    public required string RecordType { get; init; }
    public string? EditorId { get; init; }
    public required string SemanticPath { get; init; }
    public required string Category { get; init; }
    public required LocalizationDiagnosticStatus Status { get; init; }
    public required DiagnosticConfidence Confidence { get; init; }
    public required string WinningPlugin { get; init; }
    public required string WinningSourceMod { get; init; }
    public string? WinningText { get; init; }
    public string? EarlierTargetText { get; init; }
    [JsonPropertyName("earlierRussianText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyEarlierRussianText { get; init; }
    [JsonIgnore]
    public string? EarlierRussianText => EarlierTargetText ?? LegacyEarlierRussianText;
}

public sealed record DiagnosticReportSnapshot
{
    public int SchemaVersion { get; init; } = 2;
    public string SourceLanguage { get; init; } = "source";
    public string TargetLanguage { get; init; } = "target";
    public required string ReportKind { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required string IndexFingerprint { get; init; }
    public required bool Truncated { get; init; }
    public required IReadOnlyList<DiagnosticFindingSnapshot> Findings { get; init; }
}

public sealed record DiagnosticSnapshotDiff
{
    public required string BaselineFingerprint { get; init; }
    public required string CurrentFingerprint { get; init; }
    public required bool BaselineTruncated { get; init; }
    public required bool CurrentTruncated { get; init; }
    public required IReadOnlyList<DiagnosticFindingSnapshot> Added { get; init; }
    public required IReadOnlyList<DiagnosticFindingSnapshot> Resolved { get; init; }
    public required IReadOnlyList<DiagnosticFindingSnapshot> Unchanged { get; init; }
}
