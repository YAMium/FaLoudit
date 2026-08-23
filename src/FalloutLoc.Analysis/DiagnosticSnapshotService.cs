using FalloutLoc.Analysis.Models;

namespace FalloutLoc.Analysis;

public static class DiagnosticSnapshotService
{
    public static DiagnosticReportSnapshot Create(
        string reportKind,
        string indexFingerprint,
        IReadOnlyList<RecordDiagnostic> records,
        bool truncated,
        DateTime createdUtc,
        string sourceLanguage = "source",
        string targetLanguage = "target")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexFingerprint);
        ArgumentNullException.ThrowIfNull(records);
        var findings = records.SelectMany(record => record.Fields.Select(field => new DiagnosticFindingSnapshot
        {
            Identity = Identity(record.FormKey, field.SemanticPath, field.Status),
            FormKey = record.FormKey,
            RecordType = record.RecordType ?? "unknown",
            EditorId = record.EditorId,
            SemanticPath = field.SemanticPath,
            Category = field.Category,
            Status = field.Status,
            Confidence = field.Confidence,
            WinningPlugin = record.WinningPlugin ?? "unknown",
            WinningSourceMod = record.WinningSourceMod ?? "unknown",
            WinningText = field.Winner.Text,
            EarlierTargetText = field.EarlierTarget?.Text,
        }))
            .GroupBy(finding => finding.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(finding => finding.Identity, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DiagnosticReportSnapshot
        {
            ReportKind = reportKind,
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            CreatedUtc = createdUtc.ToUniversalTime(),
            IndexFingerprint = indexFingerprint,
            Truncated = truncated,
            Findings = findings,
        };
    }

    public static DiagnosticSnapshotDiff Compare(
        DiagnosticReportSnapshot baseline,
        DiagnosticReportSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        if (baseline.SchemaVersion != current.SchemaVersion
            || baseline.SchemaVersion is not (1 or 2))
        {
            throw new InvalidDataException(
                "Diagnostic snapshots must use the same supported schema version (1 or 2).");
        }

        if (baseline.SchemaVersion == 2
            && (!baseline.SourceLanguage.Equals(current.SourceLanguage, StringComparison.OrdinalIgnoreCase)
                || !baseline.TargetLanguage.Equals(current.TargetLanguage, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Cannot compare diagnostic snapshots for different language pairs.");
        }

        if (!baseline.ReportKind.Equals(current.ReportKind, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Cannot compare '{baseline.ReportKind}' and '{current.ReportKind}' diagnostic snapshots.");
        }

        var before = baseline.Findings.ToDictionary(item => item.Identity, StringComparer.OrdinalIgnoreCase);
        var after = current.Findings.ToDictionary(item => item.Identity, StringComparer.OrdinalIgnoreCase);
        return new DiagnosticSnapshotDiff
        {
            BaselineFingerprint = baseline.IndexFingerprint,
            CurrentFingerprint = current.IndexFingerprint,
            BaselineTruncated = baseline.Truncated,
            CurrentTruncated = current.Truncated,
            Added = after.Where(item => !before.ContainsKey(item.Key)).Select(item => item.Value).ToArray(),
            Resolved = before.Where(item => !after.ContainsKey(item.Key)).Select(item => item.Value).ToArray(),
            Unchanged = after.Where(item => before.ContainsKey(item.Key)).Select(item => item.Value).ToArray(),
        };
    }

    private static string Identity(
        string formKey,
        string semanticPath,
        LocalizationDiagnosticStatus status) => $"{formKey}|{semanticPath}|{status}";
}
