using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Abstractions;

public interface IRecordContentExtractor<in TRecord>
{
    IReadOnlyList<RawRecordContent> Extract(TRecord record);
}