using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;
using FalloutLoc.Core.Configuration;
using FalloutLoc.Core.IO;
using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;

namespace FalloutLoc.Backends.Mutagen;

public sealed class MutagenPluginBackend : IPluginBackend
{
    private readonly IPluginStringDecoder _decoder;
    private readonly IRecordStringExtractor<IMajorRecordGetter> _stringExtractor;
    private readonly IRecordContentExtractor<IMajorRecordGetter> _contentExtractor;

    public MutagenPluginBackend(IPluginStringDecoder decoder)
        : this(decoder, new MutagenRecordStringExtractor(), new MutagenRecordContentExtractor())
    {
    }

    internal MutagenPluginBackend(
        IPluginStringDecoder decoder,
        IRecordStringExtractor<IMajorRecordGetter> stringExtractor,
        IRecordContentExtractor<IMajorRecordGetter> contentExtractor)
    {
        _decoder = decoder;
        _stringExtractor = stringExtractor;
        _contentExtractor = contentExtractor;
    }

    public string Name => "Mutagen.Bethesda.Fallout3 0.54.4";

    public IPluginReadSession Open(PluginOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = PathRules.NormalizeAbsolute(request.Path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Plugin file does not exist.", path);
        }

        var release = request.Mode == GameMode.Fallout3
            ? Fallout3Release.Fallout3
            : Fallout3Release.FalloutNV;
        var mod = Fallout3Mod.CreateFromBinaryOverlay(
            path,
            release,
            new BinaryReadParameters { Parallel = false });
        return new Session(
            request with { Path = path },
            mod,
            _decoder,
            _stringExtractor,
            _contentExtractor);
    }

    private sealed class Session : IPluginReadSession
    {
        private readonly IFallout3ModDisposableGetter _mod;
        private readonly IPluginStringDecoder _decoder;
        private readonly IRecordStringExtractor<IMajorRecordGetter> _stringExtractor;
        private readonly IRecordContentExtractor<IMajorRecordGetter> _contentExtractor;
        private bool _disposed;

        public Session(
            PluginOpenRequest request,
            IFallout3ModDisposableGetter mod,
            IPluginStringDecoder decoder,
            IRecordStringExtractor<IMajorRecordGetter> stringExtractor,
            IRecordContentExtractor<IMajorRecordGetter> contentExtractor)
        {
            _mod = mod;
            _decoder = decoder;
            _stringExtractor = stringExtractor;
            _contentExtractor = contentExtractor;
            Metadata = new PluginMetadata
            {
                PluginName = mod.ModKey.ToString(),
                PhysicalPath = request.Path,
                Mode = request.Mode,
                LoadOrderIndex = request.LoadOrderIndex,
                SourceMod = request.SourceMod,
                Masters = mod.MasterReferences.Select(reference => reference.Master.ToString()).ToArray(),
            };
        }

        public PluginMetadata Metadata { get; }

        public IEnumerable<RecordOccurrence> EnumerateMajorRecords(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var record in _mod.EnumerateMajorRecords())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return ConvertRecord(record);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mod.Dispose();
        }

        private RecordOccurrence ConvertRecord(IMajorRecordGetter record)
        {
            var flags = ((IFallout3MajorRecordGetter)record).Fallout3MajorRecordFlags;
            var extraction = _stringExtractor.Extract(record);
            var strings = extraction.Strings
                .Select(raw =>
                {
                    var decoded = _decoder.Decode(raw.BackendValue);
                    return new RecordStringOccurrence
                    {
                        SemanticPath = raw.SemanticPath,
                        Category = raw.Category,
                        Text = decoded.Text,
                        Language = decoded.Language,
                        EncodingEvidence = decoded.EncodingEvidence,
                        RecoveredBytesSha256 = decoded.RecoveredBytesSha256,
                        Ambiguous = decoded.IsAmbiguous,
                    };
                })
                .ToArray();
            var contents = _contentExtractor.Extract(record)
                .Select(raw =>
                {
                    var decoded = _decoder.Decode(raw.BackendValue);
                    return new RecordContentOccurrence
                    {
                        SemanticPath = raw.SemanticPath,
                        SourceKind = raw.SourceKind,
                        Text = decoded.Text,
                        EncodingEvidence = decoded.EncodingEvidence,
                        RecoveredBytesSha256 = decoded.RecoveredBytesSha256,
                        Ambiguous = decoded.IsAmbiguous,
                        IsHeuristic = raw.IsHeuristic,
                    };
                })
                .ToArray();

            var status = extraction.Status;
            var warnings = extraction.Warnings;
            if (contents.Length > 0 && NormalizeRecordType(record.Registration.GetterType.Name) == "Script")
            {
                status = RecordParseStatus.PartiallyParsed;
                warnings =
                [
                    "Saved SCPT source code is indexed as untrusted content for GPT review; compiled bytecode is not yet analyzed.",
                ];
            }

            return new RecordOccurrence
            {
                FormKey = record.FormKey.ToString(),
                OriginPlugin = record.FormKey.ModKey.ToString(),
                RecordType = NormalizeRecordType(record.Registration.GetterType.Name),
                EditorId = record.EditorID,
                IsDeleted = flags.HasFlag(Fallout3MajorRecord.Fallout3MajorRecordFlag.Deleted),
                IsCompressed = flags.HasFlag(Fallout3MajorRecord.Fallout3MajorRecordFlag.Compressed),
                ParseStatus = status,
                ParseWarnings = warnings,
                Strings = strings,
                Contents = contents,
            };
        }

        private static string NormalizeRecordType(string name)
        {
            if (name.StartsWith('I'))
            {
                name = name[1..];
            }

            return name.EndsWith("Getter", StringComparison.Ordinal)
                ? name[..^"Getter".Length]
                : name;
        }
    }
}