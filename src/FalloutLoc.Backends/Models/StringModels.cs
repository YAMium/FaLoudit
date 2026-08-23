namespace FalloutLoc.Backends.Models;

public enum StringEncodingEvidence
{
    None,
    Ascii,
    UnicodeCyrillic,
    Utf8Recovered,
    Windows1251Recovered,
    SingleByteAmbiguous,
    UnrecoverableUnicode,
}

public enum TextLanguageKind
{
    Empty,
    Russian,
    English,
    Other,
}

public enum PluginEncodingClass
{
    AsciiOnlyOrNoUserText,
    Windows1251,
    Utf8,
    UnicodeCyrillic,
    SingleByteAmbiguous,
    Mixed,
    UndecodableOrNonCp1252,
}

public sealed record RawRecordString(string SemanticPath, string Category, string? BackendValue);

public enum RecordContentSourceKind
{
    EmbeddedScriptSource,
    CompiledScriptHeuristic,
    LooseFile,
    ArchiveFile,
}

public sealed record RawRecordContent(
    string SemanticPath,
    RecordContentSourceKind SourceKind,
    string? BackendValue,
    bool IsHeuristic = false);

public enum RecordParseStatus
{
    Parsed,
    PartiallyParsed,
    NotApplicable,
    Unverified,
}

public sealed record RecordStringExtractionResult
{
    public required IReadOnlyList<RawRecordString> Strings { get; init; }

    public required RecordParseStatus Status { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record DecodedString
{
    public string? Text { get; init; }

    public required TextLanguageKind Language { get; init; }

    public required StringEncodingEvidence EncodingEvidence { get; init; }

    public string? RecoveredBytesSha256 { get; init; }

    public bool IsAmbiguous => EncodingEvidence is
        StringEncodingEvidence.SingleByteAmbiguous or StringEncodingEvidence.UnrecoverableUnicode;
}

public sealed record RecordStringOccurrence
{
    public required string SemanticPath { get; init; }

    public required string Category { get; init; }

    public string? Text { get; init; }

    public required TextLanguageKind Language { get; init; }

    public required StringEncodingEvidence EncodingEvidence { get; init; }

    public string? RecoveredBytesSha256 { get; init; }

    public required bool Ambiguous { get; init; }
}

public sealed record RecordContentOccurrence
{
    public required string SemanticPath { get; init; }

    public required RecordContentSourceKind SourceKind { get; init; }

    public string? Text { get; init; }

    public required StringEncodingEvidence EncodingEvidence { get; init; }

    public string? RecoveredBytesSha256 { get; init; }

    public required bool Ambiguous { get; init; }

    public required bool IsHeuristic { get; init; }

    public bool RequiresGptReview => true;
}
public sealed record PluginEncodingSummary
{
    public required PluginEncodingClass Classification { get; init; }

    public required int TotalFields { get; init; }

    public required int AsciiFields { get; init; }

    public required int Windows1251Fields { get; init; }

    public required int Utf8Fields { get; init; }

    public required int UnicodeCyrillicFields { get; init; }

    public required int AmbiguousFields { get; init; }

    public required int UnrecoverableFields { get; init; }
}
