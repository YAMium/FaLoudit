using System.Buffers.Binary;
using System.Text;
using FalloutLoc.Backends.Engine;

namespace FalloutLoc.Backends.Tests;

public sealed class EngineGameSettingTests
{
    [Fact]
    public void ExtractsNameValuePairsFromStaticGeckConstructors()
    {
        var path = FixturePath("synthetic-geck.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, BuildSyntheticGeck());
        try
        {
            var catalog = new GeckGameSettingCatalogExtractor().Extract(path);

            Assert.Equal(100, catalog.Entries.Count);
            Assert.Equal(
                "How many?",
                Assert.Single(catalog.Entries, entry => entry.EditorId == "sHowMany").DefaultValue);
            Assert.Equal(
                "Value 099",
                Assert.Single(catalog.Entries, entry => entry.EditorId == "sFixture099").DefaultValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsExecutablesWithoutACredibleCatalog()
    {
        var path = FixturePath("invalid-geck.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, BuildSyntheticGeck(settingCount: 1));
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                new GeckGameSettingCatalogExtractor().Extract(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StewieParserReadsOnlyStringGameSettingsAndUsesLastAssignment()
    {
        var entries = new StewieGameSettingIniParser().Parse(
        [
            "sOutside = ignored",
            "[GameSettings]",
            "fNumeric = 1.0",
            "sHowMany = First",
            "sHowMany = \"Сколько?\"",
            "; sComment = ignored",
            "[Tweaks]",
            "sAfter = ignored",
        ]);

        var entry = Assert.Single(entries);
        Assert.Equal("sHowMany", entry.EditorId);
        Assert.Equal("Сколько?", entry.Value);
        Assert.Equal(5, entry.LineNumber);
    }

    private static byte[] BuildSyntheticGeck(int settingCount = 100)
    {
        const int peOffset = 0x80;
        const int optionalHeaderSize = 0xE0;
        const int sectionTableOffset = peOffset + 24 + optionalHeaderSize;
        const uint imageBase = 0x00400000;
        const int textRaw = 0x400;
        const uint textRva = 0x1000;
        const int rdataRaw = 0x1400;
        const uint rdataRva = 0x3000;
        const int dataRaw = 0x3400;
        const uint dataRva = 0x5000;
        var bytes = new byte[0x4400];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        WriteInt32(bytes, 0x3C, peOffset);
        bytes[peOffset] = (byte)'P';
        bytes[peOffset + 1] = (byte)'E';
        WriteUInt16(bytes, peOffset + 4, 0x14C);
        WriteUInt16(bytes, peOffset + 6, 3);
        WriteUInt16(bytes, peOffset + 20, optionalHeaderSize);
        WriteUInt16(bytes, peOffset + 24, 0x10B);
        WriteUInt32(bytes, peOffset + 24 + 28, imageBase);
        WriteSection(bytes, sectionTableOffset, ".text", textRva, 0x1000, textRaw, 0x1000);
        WriteSection(bytes, sectionTableOffset + 40, ".rdata", rdataRva, 0x2000, rdataRaw, 0x2000);
        WriteSection(bytes, sectionTableOffset + 80, ".data", dataRva, 0x1000, dataRaw, 0x1000);

        var stringOffset = rdataRaw;
        for (var index = 0; index < settingCount; index++)
        {
            var name = index == 0 ? "sHowMany" : $"sFixture{index:000}";
            var value = index == 0 ? "How many?" : $"Value {index:000}";
            var nameOffset = stringOffset;
            stringOffset = WriteAscii(bytes, stringOffset, name);
            var valueOffset = stringOffset;
            stringOffset = WriteAscii(bytes, stringOffset, value);

            var instructionOffset = textRaw + (index * 20);
            var instructionVa = imageBase + textRva + (uint)(instructionOffset - textRaw);
            var constructorVa = imageBase + textRva + 0xF00;
            bytes[instructionOffset] = 0x68;
            WriteUInt32(bytes, instructionOffset + 1, ToVa(nameOffset: valueOffset));
            bytes[instructionOffset + 5] = 0x68;
            WriteUInt32(bytes, instructionOffset + 6, ToVa(nameOffset));
            bytes[instructionOffset + 10] = 0xB9;
            WriteUInt32(bytes, instructionOffset + 11, imageBase + dataRva + (uint)(index * 12));
            bytes[instructionOffset + 15] = 0xE8;
            WriteInt32(bytes, instructionOffset + 16, checked((int)(constructorVa - (instructionVa + 20))));
        }

        // A byte-identical false positive outside a mapped section must be ignored rather
        // than making the whole catalog fail because its file offset has no virtual address.
        const int unmappedPatternOffset = 0x220;
        bytes[unmappedPatternOffset] = 0x68;
        WriteUInt32(bytes, unmappedPatternOffset + 1, ToVa(rdataRaw + 9));
        bytes[unmappedPatternOffset + 5] = 0x68;
        WriteUInt32(bytes, unmappedPatternOffset + 6, ToVa(rdataRaw));
        bytes[unmappedPatternOffset + 10] = 0xB9;
        WriteUInt32(bytes, unmappedPatternOffset + 11, imageBase + dataRva);
        bytes[unmappedPatternOffset + 15] = 0xE8;

        return bytes;

        static uint ToVa(int nameOffset) => imageBase + rdataRva + (uint)(nameOffset - rdataRaw);
    }

    private static int WriteAscii(byte[] bytes, int offset, string value)
    {
        var encoded = System.Text.Encoding.ASCII.GetBytes(value);
        encoded.CopyTo(bytes, offset);
        bytes[offset + encoded.Length] = 0;
        return offset + encoded.Length + 1;
    }

    private static void WriteSection(
        byte[] bytes,
        int offset,
        string name,
        uint virtualAddress,
        uint virtualSize,
        int rawOffset,
        uint rawSize)
    {
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, offset);
        WriteUInt32(bytes, offset + 8, virtualSize);
        WriteUInt32(bytes, offset + 12, virtualAddress);
        WriteUInt32(bytes, offset + 16, rawSize);
        WriteUInt32(bytes, offset + 20, checked((uint)rawOffset));
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private static string FixturePath(string fileName) => Path.Combine(
        Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".falloutloc", "fixtures", "engine-tests")),
        Guid.NewGuid().ToString("N"),
        fileName);
}
