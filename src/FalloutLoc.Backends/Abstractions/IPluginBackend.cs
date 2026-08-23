using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Abstractions;

public interface IPluginBackend
{
    string Name { get; }

    IPluginReadSession Open(PluginOpenRequest request);
}

public interface IRecordEnumerator
{
    IEnumerable<RecordOccurrence> EnumerateMajorRecords(CancellationToken cancellationToken = default);
}

public interface IPluginReadSession : IDisposable, IRecordEnumerator
{
    PluginMetadata Metadata { get; }
}
