using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Abstractions;

public interface IRecordStringExtractor<in TRecord>
{
    RecordStringExtractionResult Extract(TRecord record);
}
