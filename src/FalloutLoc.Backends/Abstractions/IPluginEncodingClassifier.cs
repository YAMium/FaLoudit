using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Abstractions;

public interface IPluginEncodingClassifier
{
    PluginEncodingSummary Classify(IEnumerable<RecordStringOccurrence> strings);
}
