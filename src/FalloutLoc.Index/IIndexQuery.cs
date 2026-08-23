using FalloutLoc.Index.Models;

namespace FalloutLoc.Index;

public interface IIndexQuery
{
    IReadOnlyList<IndexedStringMatch> Find(string query, int limit = 50);

    IndexedPage<IndexedStringMatch> SearchText(IndexedTextSearchRequest request);

    IndexedPage<IndexedContentMatch> SearchContent(IndexedContentSearchRequest request) => new()
    {
        Items = [],
        Limit = request.Limit,
        HasMore = false,
    };

    IndexedPage<IndexedRecordMatch> FindByEditorId(IndexedEditorIdSearchRequest request);

    IndexedFormLookupResult ResolveForm(string input, int limit = 50, string? cursor = null);

    IndexedOverrideTrace Trace(string formKey);

    IReadOnlyList<string> FindRegressionCandidateFormKeys(string? winningPlugin, int limit);

    IReadOnlyList<string> FindUntranslatedCandidateFormKeys(string? winningPlugin, int limit);

    IndexedPage<string> FindDiagnosticCandidateFormKeys(IndexedDiagnosticCandidateRequest request);

    IReadOnlyList<IndexedPhysicalProvider> GetPhysicalProviders(string logicalPath);

    IndexSnapshotStatus GetStatus();

    IndexCoverageReport GetCoverage(int issueLimit = 100);
}
