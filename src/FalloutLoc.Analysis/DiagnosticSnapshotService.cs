using FalloutLoc.Analysis.Models;

namespace FalloutLoc.Analysis;

public static class DiagnosticSnapshotService
{
    public static DiagnosticReportSnapshot Create(
        string reportKind,
        string indexFingerprint,
        IReadOnlyList<RecordDiagnostic> records,
        bool truncated,
        DateTime createdUtc)
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
            EarlierRussianText = field.EarlierRussian?.Text,
        }))
            .GroupBy(finding => finding.Identity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(finding => finding.Identity, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DiagnosticReportSnapshot
        {
            ReportKind = reportKind,
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
        if (baseline.SchemaVersion != 1 || current.SchemaVersion != 1)
        {
            throw new InvalidDataException("Only diagnostic snapshot schema version 1 is supported.");
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
