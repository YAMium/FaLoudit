using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Encoding;

public sealed class PluginEncodingClassifier : IPluginEncodingClassifier
{
    public PluginEncodingSummary Classify(IEnumerable<RecordStringOccurrence> strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        var materialized = strings.ToArray();
        var ascii = Count(StringEncodingEvidence.Ascii);
        var windows1251 = Count(StringEncodingEvidence.Windows1251Recovered);
        var utf8 = Count(StringEncodingEvidence.Utf8Recovered);
        var unicode = Count(StringEncodingEvidence.UnicodeCyrillic);
        var ambiguous = Count(StringEncodingEvidence.SingleByteAmbiguous);
        var unrecoverable = Count(StringEncodingEvidence.UnrecoverableUnicode);
        var positiveEncodingClasses = new[] { windows1251, utf8, unicode }.Count(count => count > 0);

        var classification = unrecoverable > 0
            ? PluginEncodingClass.UndecodableOrNonCp1252
            : positiveEncodingClasses > 1
                ? PluginEncodingClass.Mixed
                : windows1251 > 0
                    ? PluginEncodingClass.Windows1251
                    : utf8 > 0
                        ? PluginEncodingClass.Utf8
                        : unicode > 0
                            ? PluginEncodingClass.UnicodeCyrillic
                            : ambiguous > 0
                                ? PluginEncodingClass.SingleByteAmbiguous
                                : PluginEncodingClass.AsciiOnlyOrNoUserText;

        return new PluginEncodingSummary
        {
            Classification = classification,
            TotalFields = materialized.Length,
            AsciiFields = ascii,
            Windows1251Fields = windows1251,
            Utf8Fields = utf8,
            UnicodeCyrillicFields = unicode,
            AmbiguousFields = ambiguous,
            UnrecoverableFields = unrecoverable,
        };

        int Count(StringEncodingEvidence evidence) =>
            materialized.Count(field => field.EncodingEvidence == evidence);
    }
}
