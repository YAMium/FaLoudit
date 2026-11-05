using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;

namespace FalloutLoc.Backends.Mutagen;

/// <summary>Reads SCTX through Mutagen's framing API when its typed SCPT reader loses XXXX-sized source.</summary>
internal sealed class ScriptSourceRecovery(string path, GameRelease release) : IDisposable
{
    private MutagenBinaryReadStream? _stream;
    private RecordLocatorResults? _locations;
    private Exception? _initializationError;

    public string? Read(FormKey key)
    {
        if (_initializationError is not null)
            throw new InvalidDataException("SCPT source locator is unavailable.", _initializationError);
        if (_stream is null)
        {
            var modPath = new ModPath(ModKey.FromNameAndExtension(Path.GetFileName(path)), path);
            try
            {
                _stream = new MutagenBinaryReadStream(modPath, release, masterFlagLookup: null);
                _locations = RecordLocator.GetLocations(_stream, new RecordInterest(new RecordType("SCPT")));
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                _stream?.Dispose();
                _stream = null;
                _initializationError = exception;
                throw;
            }
        }

        _stream.Position = _locations!.GetRecord(key).Location.Min;
        var frame = _stream.ReadMajorRecord(automaticallyDecompress: true);
        var sources = frame.EnumerateSubrecords()
            .Where(sub => sub.RecordType == new RecordType("SCTX"))
            .ToArray();
        if (sources.Length == 0) return null;
        if (sources.Length != 1) throw new InvalidDataException("Ambiguous SCPT: multiple SCTX sources.");
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252).GetString(sources[0].Content.Span).TrimEnd('\0');
    }

    public void Dispose() => _stream?.Dispose();
}
