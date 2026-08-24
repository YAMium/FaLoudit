using FalloutLoc.Backends.Models;
using FalloutLoc.Core.Configuration;
using System.Text.Json.Serialization;

namespace FalloutLoc.Index.Models;

public sealed record IndexPluginInput
{
    public required int LoadOrderIndex { get; init; }
    public required string Name { get; init; }
    public required string PhysicalPath { get; init; }
    public required string SourceMod { get; init; }
    public required long EffectivePriority { get; init; }
    public required long FileLength { get; init; }
    public required DateTime LastWriteUtc { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record IndexPhysicalProviderInput
{
    public required string LogicalPath { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceName { get; init; }
    public required long EffectivePriority { get; init; }
    public int? ProfileLine { get; init; }
    public required string PhysicalPath { get; init; }
    public required bool IsWinner { get; init; }
    public required long FileLength { get; init; }
    public required DateTime LastWriteUtc { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record IndexEngineGameSettingInput
{
    public required string EditorId { get; init; }
    public required string DefaultText { get; init; }
    public required TextLanguageKind Language { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
}

public sealed record IndexPostPluginGameSettingInput
{
    public required string EditorId { get; init; }
    public required string Text { get; init; }
    public required TextLanguageKind Language { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
    public required string LogicalPath { get; init; }
    public required string PhysicalPath { get; init; }
    public required string SourceMod { get; init; }
    public required long EffectivePriority { get; init; }
    public required int Sequence { get; init; }
}

public sealed record IndexLooseContentEntryInput
{
    public required string SemanticPath { get; init; }
    public required string Text { get; init; }
    public required string Context { get; init; }
    public required int LineNumber { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
    public required bool IsHeuristic { get; init; }
}

public sealed record IndexLooseContentFileInput
{
    public required string LogicalPath { get; init; }
    public required string PhysicalPath { get; init; }
    public required string SourceMod { get; init; }
    public required long EffectivePriority { get; init; }
    public required RecordContentSourceKind SourceKind { get; init; }
    public required long FileLength { get; init; }
    public required DateTime LastWriteUtc { get; init; }
    public string? Sha256 { get; init; }
    public IReadOnlyList<IndexLooseContentEntryInput> Entries { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record IndexBuildRequest
{
    public required string DestinationPath { get; init; }
    public required GameMode Mode { get; init; }
    public required string Mo2Root { get; init; }
    public required string ProfileName { get; init; }
    public required string LoadOrderFingerprint { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required IReadOnlyList<IndexPluginInput> Plugins { get; init; }
    public required IReadOnlyList<IndexPhysicalProviderInput> PhysicalProviders { get; init; }
    public IReadOnlyList<IndexEngineGameSettingInput> EngineGameSettings { get; init; } = [];
    public IReadOnlyList<IndexPostPluginGameSettingInput> PostPluginGameSettings { get; init; } = [];
    public IReadOnlyList<IndexLooseContentFileInput> LooseContentFiles { get; init; } = [];
    public IReadOnlyList<string> LooseContentWarnings { get; init; } = [];
    public string EngineGameSettingCatalogStatus { get; init; } = "unavailable";
    public string? EngineGameSettingCatalogPath { get; init; }
    public string? RuntimeExecutablePath { get; init; }
    public IReadOnlyList<string> EngineGameSettingWarnings { get; init; } = [];
    public string? PreviousDatabasePath { get; init; }
    public bool ReuseUnchangedPlugins { get; init; } = true;
}

public sealed record IndexProgress
{
    public required int CompletedPlugins { get; init; }
    public required int TotalPlugins { get; init; }
    public required string PluginName { get; init; }
    public required string ParseStatus { get; init; }
    public required long TotalRecords { get; init; }
    public required long TotalStrings { get; init; }
    public long TotalContents { get; init; }
}

public sealed record IndexBuildResult
{
    public required string DatabasePath { get; init; }
    public required int SchemaVersion { get; init; }
    public required int ParsedPlugins { get; init; }
    public required int ReusedPlugins { get; init; }
    public int IndexedPlugins => ParsedPlugins + ReusedPlugins;
    public required int FailedPlugins { get; init; }
    public int PartiallyParsedPlugins { get; init; }
    public long CoverageGapRecords { get; init; }
    public required long Records { get; init; }
    public required long Strings { get; init; }
    public long Contents { get; init; }
    public int EngineGameSettings { get; init; }
    public int PostPluginGameSettingOverrides { get; init; }
    public int LooseContentFiles { get; init; }
    public int LooseContentEntries { get; init; }
    public IReadOnlyList<string> LooseContentWarnings { get; init; } = [];
    public required string EngineGameSettingCatalogStatus { get; init; }
    public IReadOnlyList<string> EngineGameSettingWarnings { get; init; } = [];
    public required TimeSpan Duration { get; init; }
}

public sealed record IndexedStringMatch
{
    public required string FormKey { get; init; }
    public required string RecordType { get; init; }
    public string? EditorId { get; init; }
    public required string PluginName { get; init; }
    public required int LoadOrderIndex { get; init; }
    public required string PhysicalPath { get; init; }
    public required string SourceMod { get; init; }
    public required string SemanticPath { get; init; }
    public required string Category { get; init; }
    public string? Text { get; init; }
    public required TextLanguageKind Language { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
    public required bool IsWinningOverride { get; init; }
    public string SourceKind { get; init; } = "plugin";
}

public enum IndexedTextSearchMode
{
    Contains,
    Exact,
    Regex,
}

public sealed record IndexedTextSearchRequest
{
    public required string Query { get; init; }
    public IndexedTextSearchMode Mode { get; init; } = IndexedTextSearchMode.Contains;
    public bool IgnoreCase { get; init; }
    public string? PluginName { get; init; }
    public string? RecordType { get; init; }
    public string? Category { get; init; }
    public bool WinnerOnly { get; init; }
    public int Limit { get; init; } = 50;
    public string? Cursor { get; init; }
}

public sealed record IndexedContentMatch
{
    public required string FormKey { get; init; }
    public required string RecordType { get; init; }
    public string? EditorId { get; init; }
    public required string PluginName { get; init; }
    public required int LoadOrderIndex { get; init; }
    public required string PhysicalPath { get; init; }
    public required string SourceMod { get; init; }
    public required long EffectivePriority { get; init; }
    public required string SemanticPath { get; init; }
    public required RecordContentSourceKind SourceKind { get; init; }
    public required string Context { get; init; }
    public required int ContextStart { get; init; }
    public required int ContentLength { get; init; }
    public int? LineNumber { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
    public required bool IsHeuristic { get; init; }
    public required bool IsWinningOverride { get; init; }
    public bool IsUntrustedContent => true;
    public bool RequiresGptReview => true;
}

public sealed record IndexedContentSearchRequest
{
    public required string Query { get; init; }
    public IndexedTextSearchMode Mode { get; init; } = IndexedTextSearchMode.Contains;
    public bool IgnoreCase { get; init; }
    public string? PluginName { get; init; }
    public string? RecordType { get; init; }
    public RecordContentSourceKind? SourceKind { get; init; }
    public bool WinnerOnly { get; init; }
    public int Limit { get; init; } = 20;
    public string? Cursor { get; init; }
}
public sealed record IndexedEditorIdSearchRequest
{
    public required string EditorId { get; init; }
    public string? PluginName { get; init; }
    public string? RecordType { get; init; }
    public bool WinnerOnly { get; init; }
    public int Limit { get; init; } = 50;
    public string? Cursor { get; init; }
}

public enum IndexedDiagnosticKind
{
    Regressions,
    Untranslated,
}

public sealed record IndexedDiagnosticCandidateRequest
{
    public required IndexedDiagnosticKind Kind { get; init; }
    public string? WinningPlugin { get; init; }
    public string? SourceMod { get; init; }
    public string? RecordType { get; init; }
    public string? Category { get; init; }
    public int Limit { get; init; } = 100;
    public string? Cursor { get; init; }
}

public sealed record IndexedPage<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Limit { get; init; }
    public required bool HasMore { get; init; }
    public string? NextCursor { get; init; }
}

public sealed record IndexedRecordMatch
{
    public required string FormKey { get; init; }
    public required string OriginPlugin { get; init; }
    public required string RecordType { get; init; }
    public string? EditorId { get; init; }
    public required string WinningPluginName { get; init; }
    public required int WinningLoadOrderIndex { get; init; }
    public required string WinningPhysicalPath { get; init; }
    public required string WinningSourceMod { get; init; }
    public required long WinningEffectivePriority { get; init; }
    public required bool IsDeleted { get; init; }
    public required int OverrideCount { get; init; }
}

public enum IndexedFormLookupKind
{
    FormKey,
    RuntimeFormId,
    LocalFormId,
}

public sealed record IndexedFormLookupResult
{
    public required string Input { get; init; }
    public required IndexedFormLookupKind Kind { get; init; }
    public required string LocalFormId { get; init; }
    public int? RuntimeLoadOrderIndex { get; init; }
    public string? ResolvedOriginPlugin { get; init; }
    public required bool IsAmbiguous { get; init; }
    public required IndexedPage<IndexedRecordMatch> Matches { get; init; }
}

public sealed record IndexedTraceString
{
    public required string SemanticPath { get; init; }
    public required string Category { get; init; }
    public string? Text { get; init; }
    public required TextLanguageKind Language { get; init; }
    public required StringEncodingEvidence EncodingEvidence { get; init; }
    public required bool Ambiguous { get; init; }
}

public sealed record IndexedOverride
{
    public required string PluginName { get; init; }
    public required int LoadOrderIndex { get; init; }
    public required string PhysicalPath { get; init; }
    public required string SourceMod { get; init; }
    public required long EffectivePriority { get; init; }
    public required string RecordType { get; init; }
    public string? EditorId { get; init; }
    public required bool IsDeleted { get; init; }
    public required bool IsCompressed { get; init; }
    public required bool IsWinner { get; init; }
    public required IReadOnlyList<IndexedTraceString> Strings { get; init; }
}

public sealed record IndexedOverrideTrace
{
    public required string FormKey { get; init; }
    public required IReadOnlyList<IndexedOverride> Chain { get; init; }
}

public sealed record IndexedPhysicalProvider
{
    public required string LogicalPath { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceName { get; init; }
    public required long EffectivePriority { get; init; }
    public int? ProfileLine { get; init; }
    public required string PhysicalPath { get; init; }
    public required bool IsWinner { get; init; }
}

public sealed record IndexSnapshotStatus
{
    public required int SchemaVersion { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required GameMode Mode { get; init; }
    public required string ProfileName { get; init; }
    public required string LoadOrderFingerprint { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required string BackendName { get; init; }
    public required int ParsedPlugins { get; init; }
    public required int FailedPlugins { get; init; }
    public int PartiallyParsedPlugins { get; init; }
    public long CoverageGapRecords { get; init; }
    public int EngineGameSettings { get; init; }
    public int PostPluginGameSettingOverrides { get; init; }
    public string EngineGameSettingCatalogStatus { get; init; } = "unavailable";
    public string? EngineGameSettingCatalogPath { get; init; }
    public string? RuntimeExecutablePath { get; init; }
    public IReadOnlyList<string> EngineGameSettingWarnings { get; init; } = [];
    public int LooseContentFiles { get; init; }
    public int LooseContentEntries { get; init; }
    public IReadOnlyList<string> LooseContentWarnings { get; init; } = [];
}

public sealed record IndexCoverageRecordType
{
    public required string RecordType { get; init; }
    public required long TotalRecords { get; init; }
    public required long ParsedRecords { get; init; }
    public required long PartiallyParsedRecords { get; init; }
    public required long NotApplicableRecords { get; init; }
    public required long UnverifiedRecords { get; init; }
    public required long RecordsWithStrings { get; init; }
    public required long StringFields { get; init; }
    public required long NonEmptyStringFields { get; init; }
}

public sealed record IndexCoverageCategory
{
    public required string Category { get; init; }
    public required long Fields { get; init; }
    public required long NonEmptyFields { get; init; }
    public required long TargetFields { get; init; }
    public required long SourceFields { get; init; }
    public required long AmbiguousFields { get; init; }

    [JsonIgnore]
    public long RussianFields => TargetFields;
    [JsonIgnore]
    public long EnglishFields => SourceFields;
}

public sealed record IndexCoverageIssue
{
    public required string PluginName { get; init; }
    public required string FormKey { get; init; }
    public required string RecordType { get; init; }
    public string? EditorId { get; init; }
    public required RecordParseStatus Status { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record IndexCoverageReport
{
    public required string CatalogVersion { get; init; }
    public required IReadOnlyList<LocalizationFieldDefinition> SupportedFields { get; init; }
    public required int SchemaVersion { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required GameMode Mode { get; init; }
    public required string ProfileName { get; init; }
    public required string LoadOrderFingerprint { get; init; }
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required int TotalPlugins { get; init; }
    public required int ParsedPlugins { get; init; }
    public required int PartiallyParsedPlugins { get; init; }
    public required int FailedPlugins { get; init; }
    public required long TotalRecords { get; init; }
    public required long ParsedRecords { get; init; }
    public required long PartiallyParsedRecords { get; init; }
    public required long NotApplicableRecords { get; init; }
    public required long UnverifiedRecords { get; init; }
    public required long TotalStringFields { get; init; }
    public required long NonEmptyStringFields { get; init; }
    public required long AmbiguousStringFields { get; init; }
    public int EngineGameSettings { get; init; }
    public int PostPluginGameSettingOverrides { get; init; }
    public string EngineGameSettingCatalogStatus { get; init; } = "unavailable";
    public int LooseContentFiles { get; init; }
    public int LooseContentEntries { get; init; }
    public required bool IssuesTruncated { get; init; }
    public required IReadOnlyList<IndexCoverageRecordType> RecordTypes { get; init; }
    public required IReadOnlyList<IndexCoverageCategory> Categories { get; init; }
    public required IReadOnlyList<IndexCoverageIssue> Issues { get; init; }
}
