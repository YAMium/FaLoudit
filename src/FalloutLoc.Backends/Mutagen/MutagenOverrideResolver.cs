using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Mutagen;

public sealed class MutagenOverrideResolver(IPluginBackend backend) : IOverrideResolver
{
    public OverrideTraceResult Trace(
        OverrideTraceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FormKey);
        var chain = new List<OverrideOccurrence>();

        foreach (var plugin in request.ActivePlugins.OrderBy(plugin => plugin.LoadOrderIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var session = backend.Open(new PluginOpenRequest
            {
                Path = plugin.PhysicalPath,
                Mode = request.Mode,
                LoadOrderIndex = plugin.LoadOrderIndex,
                SourceMod = plugin.SourceMod,
            });
            var record = session.EnumerateMajorRecords(cancellationToken)
                .FirstOrDefault(candidate => candidate.FormKey.Equals(request.FormKey, StringComparison.OrdinalIgnoreCase));
            if (record is not null)
            {
                chain.Add(new OverrideOccurrence { Plugin = plugin, Record = record });
            }
        }

        return new OverrideTraceResult { FormKey = request.FormKey, Chain = chain };
    }
}
