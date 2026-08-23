using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace FalloutLoc.Backends.Engine;

/// <summary>
/// Reads the static string GameSetting constructor table from a local FO3/FNV GECK executable.
/// The executable is treated strictly as a read-only data source and is never loaded or executed.
/// </summary>
public sealed partial class GeckGameSettingCatalogExtractor
{
    public const string Version = "1";

    private const int MaximumPeSections = 96;
    private const int MaximumSettingNameBytes = 256;
    private const int MaximumSettingValueBytes = 8192;
    private const int MinimumCredibleCatalogEntries = 100;

    public GeckGameSettingCatalog Extract(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var path = Path.GetFullPath(executablePath);
        var bytes = File.ReadAllBytes(path);
        var image = PortableExecutableImage.Parse(bytes);
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = System.Text.Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        var candidates = new List<SettingConstructorCandidate>();

        // The FO3/FNV GECK initializes string settings with this x86 sequence:
        // push value; push name; mov ecx, setting-object; call Setting::.ctor.
        for (var offset = 0; offset <= bytes.Length - 20; offset++)
        {
            if (bytes[offset] != 0x68
                || bytes[offset + 5] != 0x68
                || bytes[offset + 10] != 0xB9
                || bytes[offset + 15] != 0xE8)
            {
                continue;
            }

            var valueAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 1, 4));
            var nameAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 6, 4));
            var objectAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 11, 4));
            if (!image.ContainsVirtualAddress(objectAddress))
            {
                continue;
            }

            var instructionAddress = image.TryFileOffsetToVirtualAddress(offset);
            if (instructionAddress is null)
            {
                continue;
            }

            var callDisplacement = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 16, 4));
            var callTarget = instructionAddress.Value + 20L + callDisplacement;
            if (callTarget < uint.MinValue || callTarget > uint.MaxValue
                || !image.ContainsVirtualAddress((uint)callTarget))
            {
                continue;
            }

            if (!TryReadNullTerminatedString(
                    bytes,
                    image.VirtualAddressToFileOffset(nameAddress),
                    MaximumSettingNameBytes,
                    encoding,
                    out var name)
                || !StringGameSettingNameRegex().IsMatch(name)
                || !TryReadNullTerminatedString(
                    bytes,
                    image.VirtualAddressToFileOffset(valueAddress),
                    MaximumSettingValueBytes,
                    encoding,
                    out var value))
            {
                continue;
            }

            candidates.Add(new SettingConstructorCandidate(name, value, (uint)callTarget));
        }

        var selectedConstructors = candidates
            .GroupBy(candidate => candidate.CallTarget)
            .OrderByDescending(group => group
                .Select(candidate => candidate.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count())
            .FirstOrDefault();
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in selectedConstructors?.AsEnumerable() ?? [])
        {
            if (entries.TryGetValue(candidate.Name, out var existing))
            {
                if (!existing.Equals(candidate.Value, StringComparison.Ordinal))
                {
                    conflicts.Add(candidate.Name);
                }

                continue;
            }

            entries.Add(candidate.Name, candidate.Value);
        }

        if (entries.Count < MinimumCredibleCatalogEntries)
        {
            throw new InvalidDataException(
                $"GECK executable did not expose a credible string GameSetting catalog: " +
                $"found {entries.Count}, expected at least {MinimumCredibleCatalogEntries}.");
        }

        if (conflicts.Count > 0)
        {
            throw new InvalidDataException(
                $"GECK executable contains conflicting defaults for {conflicts.Count} string GameSetting(s): " +
                string.Join(", ", conflicts.Order(StringComparer.OrdinalIgnoreCase).Take(10)));
        }

        return new GeckGameSettingCatalog
        {
            ExecutablePath = path,
            Entries = entries
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new GeckGameSettingCatalogEntry(item.Key, item.Value))
                .ToArray(),
        };
    }

    private static bool TryReadNullTerminatedString(
        byte[] bytes,
        int? offset,
        int maximumBytes,
        System.Text.Encoding encoding,
        out string value)
    {
        value = string.Empty;
        if (offset is null || offset < 0 || offset >= bytes.Length)
        {
            return false;
        }

        var end = offset.Value;
        var limit = Math.Min(bytes.Length, checked(offset.Value + maximumBytes + 1));
        while (end < limit && bytes[end] != 0)
        {
            end++;
        }

        if (end == limit)
        {
            return false;
        }

        try
        {
            value = encoding.GetString(bytes, offset.Value, end - offset.Value);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[sS][A-Za-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex StringGameSettingNameRegex();

    private sealed class PortableExecutableImage
    {
        private readonly byte[] _bytes;
        private readonly PeSection[] _sections;

        private PortableExecutableImage(byte[] bytes, uint imageBase, PeSection[] sections)
        {
            _bytes = bytes;
            ImageBase = imageBase;
            _sections = sections;
        }

        public uint ImageBase { get; }

        public static PortableExecutableImage Parse(byte[] bytes)
        {
            if (bytes.Length < 0x100 || bytes[0] != 'M' || bytes[1] != 'Z')
            {
                throw new InvalidDataException("GECK catalog source is not a valid DOS/PE executable.");
            }

            var peOffset = ReadInt32(bytes, 0x3C);
            if (peOffset < 0 || peOffset > bytes.Length - 24
                || bytes[peOffset] != 'P' || bytes[peOffset + 1] != 'E'
                || bytes[peOffset + 2] != 0 || bytes[peOffset + 3] != 0)
            {
                throw new InvalidDataException("GECK catalog source has an invalid PE header.");
            }

            var sectionCount = ReadUInt16(bytes, peOffset + 6);
            var optionalHeaderSize = ReadUInt16(bytes, peOffset + 20);
            var optionalHeaderOffset = checked(peOffset + 24);
            if (sectionCount is 0 or > MaximumPeSections
                || optionalHeaderSize < 32
                || optionalHeaderOffset > bytes.Length - optionalHeaderSize
                || ReadUInt16(bytes, optionalHeaderOffset) != 0x10B)
            {
                throw new InvalidDataException("GECK catalog extraction requires a valid PE32/x86 executable.");
            }

            var imageBase = ReadUInt32(bytes, optionalHeaderOffset + 28);
            var sectionTableOffset = checked(optionalHeaderOffset + optionalHeaderSize);
            if (sectionTableOffset > bytes.Length - checked(sectionCount * 40))
            {
                throw new InvalidDataException("GECK catalog source has a truncated PE section table.");
            }

            var sections = new PeSection[sectionCount];
            for (var index = 0; index < sectionCount; index++)
            {
                var offset = checked(sectionTableOffset + (index * 40));
                var virtualSize = ReadUInt32(bytes, offset + 8);
                var virtualAddress = ReadUInt32(bytes, offset + 12);
                var rawSize = ReadUInt32(bytes, offset + 16);
                var rawOffset = ReadUInt32(bytes, offset + 20);
                if (rawOffset > bytes.Length || rawSize > bytes.Length - rawOffset)
                {
                    throw new InvalidDataException("GECK catalog source has an invalid PE section range.");
                }

                sections[index] = new PeSection(virtualAddress, virtualSize, rawOffset, rawSize);
            }

            return new PortableExecutableImage(bytes, imageBase, sections);
        }

        public bool ContainsVirtualAddress(uint address)
        {
            if (address < ImageBase)
            {
                return false;
            }

            var rva = address - ImageBase;
            return _sections.Any(section => section.ContainsVirtualRva(rva));
        }

        public int? VirtualAddressToFileOffset(uint address)
        {
            if (address < ImageBase)
            {
                return null;
            }

            var rva = address - ImageBase;
            foreach (var section in _sections)
            {
                if (!section.ContainsRawRva(rva))
                {
                    continue;
                }

                var result = (ulong)section.RawOffset + (rva - section.VirtualAddress);
                return result < (ulong)_bytes.Length ? checked((int)result) : null;
            }

            return null;
        }

        public uint? TryFileOffsetToVirtualAddress(int fileOffset)
        {
            foreach (var section in _sections)
            {
                if (fileOffset < section.RawOffset
                    || (ulong)fileOffset >= (ulong)section.RawOffset + section.RawSize)
                {
                    continue;
                }

                return checked(ImageBase + section.VirtualAddress + ((uint)fileOffset - section.RawOffset));
            }

            return null;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            EnsureAvailable(bytes, offset, sizeof(ushort));
            return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            EnsureAvailable(bytes, offset, sizeof(uint));
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)));
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            EnsureAvailable(bytes, offset, sizeof(int));
            return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
        }

        private static void EnsureAvailable(byte[] bytes, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length - length)
            {
                throw new InvalidDataException("GECK catalog source is truncated.");
            }
        }

        private sealed record PeSection(uint VirtualAddress, uint VirtualSize, uint RawOffset, uint RawSize)
        {
            public bool ContainsVirtualRva(uint rva) => rva >= VirtualAddress
                && (ulong)rva < (ulong)VirtualAddress + Math.Max(VirtualSize, RawSize);

            public bool ContainsRawRva(uint rva) => rva >= VirtualAddress
                && (ulong)rva < (ulong)VirtualAddress + RawSize;
        }
    }

    private sealed record SettingConstructorCandidate(string Name, string Value, uint CallTarget);
}

public sealed record GeckGameSettingCatalogEntry(string EditorId, string DefaultValue);

public sealed record GeckGameSettingCatalog
{
    public required string ExecutablePath { get; init; }

    public required IReadOnlyList<GeckGameSettingCatalogEntry> Entries { get; init; }
}
