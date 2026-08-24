using System.Text;
using FalloutLoc.Backends.Encoding;
using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Loose;

/// <summary>
/// Extracts searchable, non-executable evidence from MO2-winning loose text files.
/// Source files are read as bytes and are never loaded by the game or script runtime.
/// </summary>
public sealed class LooseContentExtractor(StrictPluginStringDecoder decoder)
{
    public const string Version = "1";
    public const int MaximumFileBytes = 4 * 1024 * 1024;

    public LooseContentExtractionResult Extract(
        string logicalPath,
        RecordContentSourceKind sourceKind,
        byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        ArgumentNullException.ThrowIfNull(bytes);
        if (sourceKind is not RecordContentSourceKind.LooseScript and not RecordContentSourceKind.IniValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind), sourceKind, "Loose extraction supports only scripts and INI values.");
        }

        if (bytes.Length > MaximumFileBytes)
        {
            return Skipped($"File exceeds the {MaximumFileBytes:N0}-byte loose-content safety limit.");
        }

        IReadOnlyList<string> lines;
        try
        {
            lines = DecodeLines(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            return Skipped($"Text decoding failed: {exception.Message}");
        }

        var entries = sourceKind == RecordContentSourceKind.IniValue
            ? ExtractIni(lines)
            : ExtractScript(lines);
        return new LooseContentExtractionResult
        {
            Entries = entries,
            Warnings = [],
        };
    }

    private IReadOnlyList<LooseContentEntry> ExtractIni(IReadOnlyList<string> lines)
    {
        var entries = new List<LooseContentEntry>();
        var section = string.Empty;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = RemoveIniInlineComment(line[(separator + 1)..]).Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                continue;
            }

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            }

            var decoded = decoder.Decode(value);
            var decodedValue = decoded.Text ?? string.Empty;

            entries.Add(CreateEntry(
                section.Length == 0 ? key : $"[{section}].{key}",
                decodedValue,
                $"{key} = {decodedValue}",
                index + 1,
                decoded));
        }

        return entries;
    }

    private static string RemoveIniInlineComment(string value)
    {
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\' && quoted)
            {
                escaped = true;
                continue;
            }

            if (current == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && (current is ';' or '#')
                && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index];
            }
        }

        return value;
    }

    private IReadOnlyList<LooseContentEntry> ExtractScript(IReadOnlyList<string> lines)
    {
        var entries = new List<LooseContentEntry>();
        for (var index = 0; index < lines.Count; index++)
        {
            var code = RemoveScriptComment(lines[index]).Trim();
            if (code.Length == 0)
            {
                continue;
            }

            var literals = ReadQuotedLiterals(code);
            var decodedLiterals = literals.Select(decoder.Decode).ToArray();
            var context = code;
            for (var literalIndex = 0; literalIndex < literals.Count; literalIndex++)
            {
                if (literals[literalIndex].Length == 0)
                {
                    continue;
                }

                context = context.Replace(
                    literals[literalIndex],
                    decodedLiterals[literalIndex].Text ?? string.Empty,
                    StringComparison.Ordinal);
            }

            for (var occurrence = 0; occurrence < literals.Count; occurrence++)
            {
                var decoded = decodedLiterals[occurrence];
                var text = decoded.Text ?? string.Empty;
                if (text.Length == 0)
                {
                    continue;
                }

                entries.Add(CreateEntry(
                    $"line[{index + 1}].literal[{occurrence + 1}]",
                    text,
                    context,
                    index + 1,
                    decoded));
            }
        }

        return entries;
    }

    private static LooseContentEntry CreateEntry(
        string semanticPath,
        string text,
        string context,
        int lineNumber,
        DecodedString decoded) => new()
        {
            SemanticPath = semanticPath,
            Text = text,
            Context = context,
            LineNumber = lineNumber,
            EncodingEvidence = decoded.EncodingEvidence,
            Ambiguous = decoded.IsAmbiguous,
            IsHeuristic = true,
        };

    private static string RemoveScriptComment(string line)
    {
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\' && quoted)
            {
                escaped = true;
                continue;
            }

            if (current == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (current == ';' && !quoted)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static IReadOnlyList<string> ReadQuotedLiterals(string line)
    {
        var result = new List<string>();
        StringBuilder? literal = null;
        var escaped = false;
        foreach (var current in line)
        {
            if (literal is null)
            {
                if (current == '"')
                {
                    literal = new StringBuilder();
                }

                continue;
            }

            if (escaped)
            {
                literal.Append(current);
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current == '"')
            {
                result.Add(literal.ToString());
                literal = null;
                continue;
            }

            literal.Append(current);
        }

        return result;
    }

    private static IReadOnlyList<string> DecodeLines(byte[] bytes)
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            text = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            text = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            text = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
        }
        else
        {
            if (bytes.AsSpan().Contains((byte)0))
            {
                throw new DecoderFallbackException("Unmarked file contains NUL bytes and is treated as binary.");
            }

            // CP1252 is a reversible byte carrier. The configured strict decoder recovers
            // legacy target/source code pages or unmarked UTF-8 one line at a time.
            text = System.Text.Encoding.GetEncoding(
                1252,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback).GetString(bytes);
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static LooseContentExtractionResult Skipped(string warning) => new()
    {
        Entries = [],
        Warnings = [warning],
    };
}

public sealed record LooseContentEntry
{
    public required string SemanticPath { get; init; }
    public required string Text { get; init; }
    public required string Context { get; init; }
    public required int LineNumber { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
    public required bool IsHeuristic { get; init; }
}

public sealed record LooseContentExtractionResult
{
    public required IReadOnlyList<LooseContentEntry> Entries { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
