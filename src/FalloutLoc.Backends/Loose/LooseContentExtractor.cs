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
    public const string Version = "2";
    public const int MaximumFileBytes = 4 * 1024 * 1024;

    public LooseContentExtractionResult Extract(
        string logicalPath,
        RecordContentSourceKind sourceKind,
        byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        ArgumentNullException.ThrowIfNull(bytes);
        if (sourceKind is not RecordContentSourceKind.LooseScript
            and not RecordContentSourceKind.IniValue
            and not RecordContentSourceKind.UiXmlText)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind), sourceKind, "Loose extraction supports scripts, INI values, and UI XML text.");
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

        var entries = sourceKind switch
        {
            RecordContentSourceKind.IniValue => ExtractIni(lines),
            RecordContentSourceKind.UiXmlText => ExtractUiXml(lines),
            _ => ExtractScript(lines),
        };
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

    private IReadOnlyList<LooseContentEntry> ExtractUiXml(IReadOnlyList<string> lines)
    {
        var entries = new List<LooseContentEntry>();
        var elements = new List<XmlElementFrame>();
        var source = string.Join('\n', lines);
        var offset = 0;
        var lineNumber = 1;
        while (offset < source.Length)
        {
            if (source.AsSpan(offset).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = source.IndexOf("-->", offset + 4, StringComparison.Ordinal);
                var next = commentEnd < 0 ? source.Length : commentEnd + 3;
                lineNumber += CountNewLines(source.AsSpan(offset, next - offset));
                offset = next;
                continue;
            }

            if (source.AsSpan(offset).StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                var contentStart = offset + 9;
                var cdataEnd = source.IndexOf("]]>", contentStart, StringComparison.Ordinal);
                var contentEnd = cdataEnd < 0 ? source.Length : cdataEnd;
                AddXmlText(
                    source.AsSpan(contentStart, contentEnd - contentStart),
                    lineNumber,
                    lines,
                    elements,
                    entries);
                var next = cdataEnd < 0 ? source.Length : cdataEnd + 3;
                lineNumber += CountNewLines(source.AsSpan(offset, next - offset));
                offset = next;
                continue;
            }

            if (source[offset] == '<')
            {
                var tagEnd = FindXmlTagEnd(source, offset + 1);
                if (tagEnd < 0)
                {
                    break;
                }

                ProcessXmlTag(source.AsSpan(offset + 1, tagEnd - offset - 1), elements);
                lineNumber += CountNewLines(source.AsSpan(offset, tagEnd - offset + 1));
                offset = tagEnd + 1;
                continue;
            }

            var nextTag = source.IndexOf('<', offset);
            if (nextTag < 0)
            {
                nextTag = source.Length;
            }

            AddXmlText(
                source.AsSpan(offset, nextTag - offset),
                lineNumber,
                lines,
                elements,
                entries);
            lineNumber += CountNewLines(source.AsSpan(offset, nextTag - offset));
            offset = nextTag;
        }

        return entries;
    }

    private void AddXmlText(
        ReadOnlySpan<char> sourceText,
        int segmentLineNumber,
        IReadOnlyList<string> lines,
        IReadOnlyList<XmlElementFrame> elements,
        ICollection<LooseContentEntry> entries)
    {
        var first = 0;
        while (first < sourceText.Length && char.IsWhiteSpace(sourceText[first]))
        {
            first++;
        }

        if (first == sourceText.Length)
        {
            return;
        }

        var last = sourceText.Length - 1;
        while (last >= first && char.IsWhiteSpace(sourceText[last]))
        {
            last--;
        }

        var lineNumber = segmentLineNumber + CountNewLines(sourceText[..first]);
        var decoded = decoder.Decode(sourceText[first..(last + 1)].ToString());
        var text = NormalizeXmlWhitespace(DecodeStandardXmlEntities(decoded.Text ?? string.Empty));
        if (text.Length == 0 || !text.Any(char.IsLetter) || IsEntitySequence(text))
        {
            return;
        }

        var element = elements.Count == 0 ? null : elements[^1];
        var occurrence = element is null ? 1 : ++element.TextOccurrences;
        var path = BuildXmlElementPath(elements);
        var rawContext = lineNumber > 0 && lineNumber <= lines.Count
            ? lines[lineNumber - 1].Trim()
            : text;
        var decodedContext = decoder.Decode(rawContext).Text ?? rawContext;
        entries.Add(CreateEntry(
            $"element[{path}].text[{occurrence}]",
            text,
            decodedContext,
            lineNumber,
            decoded));
    }

    private static void ProcessXmlTag(ReadOnlySpan<char> rawTag, IList<XmlElementFrame> elements)
    {
        var tag = rawTag.Trim();
        if (tag.Length == 0 || tag[0] is '!' or '?')
        {
            return;
        }

        if (tag[0] == '/')
        {
            var closingName = ReadXmlName(tag[1..]);
            for (var index = elements.Count - 1; index >= 0; index--)
            {
                if (!elements[index].Name.Equals(closingName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                while (elements.Count > index)
                {
                    elements.RemoveAt(elements.Count - 1);
                }

                break;
            }

            return;
        }

        var selfClosing = tag[^1] == '/';
        var name = ReadXmlName(tag);
        if (name.Length == 0 || selfClosing)
        {
            return;
        }

        elements.Add(new XmlElementFrame(name, ReadXmlAttribute(tag, "name")));
    }

    private static string ReadXmlName(ReadOnlySpan<char> value)
    {
        var length = 0;
        while (length < value.Length && !char.IsWhiteSpace(value[length]) && value[length] is not '/' and not '>')
        {
            length++;
        }

        return value[..length].ToString();
    }

    private static string? ReadXmlAttribute(ReadOnlySpan<char> tag, string attributeName)
    {
        for (var offset = 0; offset < tag.Length; offset++)
        {
            if (offset > 0 && !char.IsWhiteSpace(tag[offset - 1]))
            {
                continue;
            }

            if (!tag[offset..].StartsWith(attributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cursor = offset + attributeName.Length;
            while (cursor < tag.Length && char.IsWhiteSpace(tag[cursor]))
            {
                cursor++;
            }

            if (cursor >= tag.Length || tag[cursor] != '=')
            {
                continue;
            }

            cursor++;
            while (cursor < tag.Length && char.IsWhiteSpace(tag[cursor]))
            {
                cursor++;
            }

            if (cursor >= tag.Length || tag[cursor] is not ('"' or '\''))
            {
                continue;
            }

            var quote = tag[cursor++];
            var end = tag[cursor..].IndexOf(quote);
            return end < 0 ? null : tag.Slice(cursor, end).ToString();
        }

        return null;
    }

    private static int FindXmlTagEnd(string source, int offset)
    {
        var quote = '\0';
        for (var index = offset; index < source.Length; index++)
        {
            var current = source[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
            }
            else if (current == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static string BuildXmlElementPath(IReadOnlyList<XmlElementFrame> elements)
    {
        if (elements.Count == 0)
        {
            return "document";
        }

        return string.Join('/', elements
            .Skip(Math.Max(0, elements.Count - 3))
            .Select(element => element.NameAttribute is null
                ? element.Name
                : $"{element.Name}[name={element.NameAttribute}]"));
    }

    private static string NormalizeXmlWhitespace(string value)
    {
        var normalized = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var current in value.Trim())
        {
            if (char.IsWhiteSpace(current))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(current);
        }

        return normalized.ToString();
    }

    private static string DecodeStandardXmlEntities(string value)
    {
        var decoded = new StringBuilder(value.Length);
        for (var offset = 0; offset < value.Length; offset++)
        {
            if (value[offset] != '&')
            {
                decoded.Append(value[offset]);
                continue;
            }

            var end = value.IndexOf(';', offset + 1);
            if (end < 0)
            {
                decoded.Append(value[offset]);
                continue;
            }

            var entity = value[(offset + 1)..end];
            var replacement = entity switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                _ => DecodeNumericXmlEntity(entity),
            };
            if (replacement is null)
            {
                decoded.Append(value, offset, end - offset + 1);
            }
            else
            {
                decoded.Append(replacement);
            }

            offset = end;
        }

        return decoded.ToString();
    }

    private static string? DecodeNumericXmlEntity(string entity)
    {
        var hex = entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase);
        var number = hex ? entity[2..] : entity.StartsWith('#') ? entity[1..] : string.Empty;
        if (number.Length == 0 || !int.TryParse(
                number,
                hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var codePoint)
            || !Rune.IsValid(codePoint))
        {
            return null;
        }

        return char.ConvertFromUtf32(codePoint);
    }

    private static bool IsEntitySequence(string value)
    {
        var offset = 0;
        while (offset < value.Length)
        {
            while (offset < value.Length && char.IsWhiteSpace(value[offset]))
            {
                offset++;
            }

            if (offset == value.Length)
            {
                return true;
            }

            if (value[offset] != '&')
            {
                return false;
            }

            var end = value.IndexOf(';', offset + 1);
            if (end < 0 || end == offset + 1)
            {
                return false;
            }

            offset = end + 1;
        }

        return true;
    }

    private static int CountNewLines(ReadOnlySpan<char> value)
    {
        var count = 0;
        foreach (var current in value)
        {
            if (current == '\n')
            {
                count++;
            }
        }

        return count;
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

    private sealed record XmlElementFrame(string Name, string? NameAttribute)
    {
        public int TextOccurrences { get; set; }
    }
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
