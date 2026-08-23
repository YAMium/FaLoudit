namespace FalloutLoc.Backends.Models;

public sealed record RecordOccurrence
{
    public required string FormKey { get; init; }

    public required string OriginPlugin { get; init; }

    public required string RecordType { get; init; }

    public string? EditorId { get; init; }

    public required bool IsDeleted { get; init; }

    public required bool IsCompressed { get; init; }

    public RecordParseStatus ParseStatus { get; init; } = RecordParseStatus.Parsed;

    public IReadOnlyList<string> ParseWarnings { get; init; } = [];

    public required IReadOnlyList<RecordStringOccurrence> Strings { get; init; }

    public IReadOnlyList<RecordContentOccurrence> Contents { get; init; } = [];
}
