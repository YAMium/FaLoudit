using System.Text;
using System.Net;
using FalloutLoc.Analysis.Models;

namespace FalloutLoc.Analysis;

public static class DiagnosticReportRenderer
{
    private static readonly string[] FindingHeaders =
    [
        "FormKey", "RecordType", "EditorID", "SemanticPath", "Category", "Status", "Confidence",
        "WinningPlugin", "WinningSourceMod", "WinnerText", "EarlierTargetText",
    ];

    public static string RenderRegressionMarkdown(RegressionReport report, DateTime generatedUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = Header("Localization regression report", generatedUtc);
        text.AppendLine($"Language pair: `{Inline(report.SourceLanguage)}` -> `{Inline(report.TargetLanguage)}`  ")
            .AppendLine($"Candidate records: {report.CandidateRecords}  ")
            .AppendLine($"Affected fields: {report.Findings}  ")
            .AppendLine($"Winning plugin filter: {Inline(report.WinningPluginFilter ?? "all")}")
            .AppendLine();
        if (report.IndexHasParseFailures)
        {
            text.AppendLine("> WARNING: the index contains plugin parse failures; this report may be incomplete.")
                .AppendLine();
        }

        foreach (var record in report.Records)
        {
            AppendRecordHeader(text, record);
            foreach (var field in record.Fields.Where(field => field.EarlierTarget is not null
                         && field.Status is not LocalizationDiagnosticStatus.LocalizedTarget))
            {
                text.AppendLine($"- `{Inline(field.SemanticPath)}` — **{field.Status}** ({field.Confidence})")
                    .AppendLine($"  - Earlier target ({Inline(report.TargetLanguage)}): `{Inline(field.EarlierTarget!.PluginName)}` / {Inline(field.EarlierTarget.Text)}")
                    .AppendLine($"  - Winner: `{Inline(field.Winner.PluginName)}` / {Inline(field.Winner.Text)}")
                    .AppendLine($"  - {Inline(field.Explanation)}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    public static string RenderUntranslatedMarkdown(UntranslatedReport report, DateTime generatedUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = Header("Untranslated review candidates", generatedUtc);
        text.AppendLine($"Language pair: `{Inline(report.SourceLanguage)}` -> `{Inline(report.TargetLanguage)}`  ")
            .AppendLine($"Candidate records: {report.CandidateRecords}  ")
            .AppendLine($"Candidate fields: {report.CandidateFields}  ")
            .AppendLine($"Confidence: **{report.Confidence}**  ")
            .AppendLine($"Winning plugin filter: {Inline(report.WinningPluginFilter ?? "all")}")
            .AppendLine()
            .AppendLine($"> {Inline(report.Caveat)}")
            .AppendLine();
        if (report.IndexHasParseFailures)
        {
            text.AppendLine("> WARNING: the index contains plugin parse failures; this report may be incomplete.")
                .AppendLine();
        }

        foreach (var record in report.Records)
        {
            AppendRecordHeader(text, record);
            foreach (var field in record.Fields.Where(field =>
                         LocalizationDiagnosticService.IsUntranslatedReviewCandidate(record, field)))
            {
                text.AppendLine($"- `{Inline(field.SemanticPath)}` — {Inline(field.Winner.Text)}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    public static string RenderRegressionCsv(RegressionReport report) =>
        RenderCsv(Flatten(report.Records, report.SourceLanguage, report.TargetLanguage));

    public static string RenderUntranslatedCsv(UntranslatedReport report) =>
        RenderCsv(Flatten(report.Records, report.SourceLanguage, report.TargetLanguage));

    public static string RenderRegressionHtml(RegressionReport report, DateTime generatedUtc) =>
        RenderHtml("Localization regression report", generatedUtc,
            Flatten(report.Records, report.SourceLanguage, report.TargetLanguage));

    public static string RenderUntranslatedHtml(UntranslatedReport report, DateTime generatedUtc) =>
        RenderHtml("Untranslated review candidates", generatedUtc,
            Flatten(report.Records, report.SourceLanguage, report.TargetLanguage));

    public static string RenderDiffMarkdown(DiagnosticSnapshotDiff diff, DateTime generatedUtc)
    {
        var text = Header("Localization diagnostic snapshot comparison", generatedUtc)
            .AppendLine($"Added problems: {diff.Added.Count}  ")
            .AppendLine($"Resolved problems: {diff.Resolved.Count}  ")
            .AppendLine($"Unchanged problems: {diff.Unchanged.Count}  ")
            .AppendLine($"Baseline fingerprint: `{Inline(diff.BaselineFingerprint)}`  ")
            .AppendLine($"Current fingerprint: `{Inline(diff.CurrentFingerprint)}`")
            .AppendLine();
        if (diff.BaselineTruncated || diff.CurrentTruncated)
        {
            text.AppendLine("> WARNING: at least one snapshot is truncated; the comparison is incomplete.")
                .AppendLine();
        }

        AppendDiffSection(text, "New problems", diff.Added);
        AppendDiffSection(text, "Resolved problems", diff.Resolved);
        return text.ToString();
    }

    public static string RenderDiffCsv(DiagnosticSnapshotDiff diff)
    {
        var rows = diff.Added.Select(item => (Change: "added", Finding: item))
            .Concat(diff.Resolved.Select(item => (Change: "resolved", Finding: item)))
            .Concat(diff.Unchanged.Select(item => (Change: "unchanged", Finding: item)));
        var text = new StringBuilder("Change,").AppendLine(string.Join(',', FindingHeaders));
        foreach (var row in rows)
        {
            text.Append(Csv(row.Change)).Append(',')
                .AppendLine(string.Join(',', FindingValues(row.Finding).Select(Csv)));
        }

        return text.ToString();
    }

    public static string RenderDiffHtml(DiagnosticSnapshotDiff diff, DateTime generatedUtc)
    {
        var rows = diff.Added.Select(item => (Change: "added", Finding: item))
            .Concat(diff.Resolved.Select(item => (Change: "resolved", Finding: item)))
            .Concat(diff.Unchanged.Select(item => (Change: "unchanged", Finding: item)))
            .Select(item => new[] { item.Change }.Concat(FindingValues(item.Finding)).ToArray());
        return RenderHtmlTable(
            "Localization diagnostic snapshot comparison",
            generatedUtc,
            new[] { "Change" }.Concat(FindingHeaders).ToArray(),
            rows);
    }

    private static StringBuilder Header(string title, DateTime generatedUtc) => new StringBuilder()
        .AppendLine($"# {title}")
        .AppendLine()
        .AppendLine($"Generated UTC: {generatedUtc.ToUniversalTime():O}  ")
        .AppendLine("Source policy: active game/MO2/plugin content was read-only.")
        .AppendLine();

    private static void AppendRecordHeader(StringBuilder text, RecordDiagnostic record)
    {
        text.AppendLine($"## `{Inline(record.FormKey)}` — {Inline(record.RecordType)} / {Inline(record.EditorId)}")
            .AppendLine()
            .AppendLine($"Record winner: `{Inline(record.WinningPlugin)}`  ")
            .AppendLine($"MO2 source mod: {Inline(record.WinningSourceMod)}  ")
            .AppendLine($"Physical plugin: `{Inline(record.WinningPhysicalPath)}`")
            .AppendLine();
    }

    private static string Inline(string? value) => (value ?? "-")
        .Replace("`", "'", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static IReadOnlyList<DiagnosticFindingSnapshot> Flatten(
        IReadOnlyList<RecordDiagnostic> records,
        string sourceLanguage,
        string targetLanguage) =>
        DiagnosticSnapshotService.Create(
            "render", "render", records, false, DateTime.UnixEpoch, sourceLanguage, targetLanguage).Findings;

    private static string RenderCsv(IReadOnlyList<DiagnosticFindingSnapshot> findings)
    {
        var text = new StringBuilder().AppendLine(string.Join(',', FindingHeaders));
        foreach (var finding in findings)
        {
            text.AppendLine(string.Join(',', FindingValues(finding).Select(Csv)));
        }

        return text.ToString();
    }

    private static string RenderHtml(
        string title,
        DateTime generatedUtc,
        IReadOnlyList<DiagnosticFindingSnapshot> findings) =>
        RenderHtmlTable(title, generatedUtc, FindingHeaders, findings.Select(FindingValues));

    private static string RenderHtmlTable(
        string title,
        DateTime generatedUtc,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        var text = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(WebUtility.HtmlEncode(title)).Append("</title>")
            .Append("<style>body{font:14px system-ui,sans-serif;margin:2rem;color:#202124}")
            .Append("table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:.45rem;text-align:left;vertical-align:top}")
            .Append("th{background:#f3f4f6;position:sticky;top:0}tr:nth-child(even){background:#fafafa}</style></head><body>")
            .Append("<h1>").Append(WebUtility.HtmlEncode(title)).Append("</h1><p>Generated UTC: ")
            .Append(WebUtility.HtmlEncode(generatedUtc.ToUniversalTime().ToString("O")))
            .Append("<br>Source policy: active game/MO2/plugin content was read-only.</p><table><thead><tr>");
        foreach (var header in headers)
        {
            text.Append("<th>").Append(WebUtility.HtmlEncode(header)).Append("</th>");
        }

        text.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            text.Append("<tr>");
            foreach (var value in row)
            {
                text.Append("<td>").Append(WebUtility.HtmlEncode(value ?? string.Empty)).Append("</td>");
            }

            text.Append("</tr>");
        }

        return text.Append("</tbody></table></body></html>").ToString();
    }

    private static string?[] FindingValues(DiagnosticFindingSnapshot finding) =>
    [
        finding.FormKey, finding.RecordType, finding.EditorId, finding.SemanticPath, finding.Category,
        finding.Status.ToString(), finding.Confidence.ToString(), finding.WinningPlugin, finding.WinningSourceMod,
        finding.WinningText, finding.EarlierRussianText,
    ];

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void AppendDiffSection(
        StringBuilder text,
        string title,
        IReadOnlyList<DiagnosticFindingSnapshot> findings)
    {
        text.AppendLine($"## {title}").AppendLine();
        if (findings.Count == 0)
        {
            text.AppendLine("None.").AppendLine();
            return;
        }

        foreach (var finding in findings)
        {
            text.AppendLine($"- `{Inline(finding.FormKey)}` / `{Inline(finding.SemanticPath)}` — " +
                $"**{finding.Status}** ({finding.Confidence}), `{Inline(finding.WinningPlugin)}`: {Inline(finding.WinningText)}");
        }

        text.AppendLine();
    }
}
