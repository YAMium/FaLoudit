using System.IO.Compression;
using System.Text;
using FalloutLoc.Backends.Encoding;
using FalloutLoc.Backends.Models;
using FalloutLoc.Backends.Mutagen;
using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Backends.Tests;

public sealed class LargeScriptSourceTests
{
    [Theory]
    [InlineData(GameMode.FalloutNewVegas, false, 77000)]
    [InlineData(GameMode.Fallout3, true, 77000)]
    [InlineData(GameMode.TaleOfTwoWastelands, false, 100)]
    [InlineData(GameMode.FalloutNewVegas, false, 0)]
    public void ReadsSavedSourceIncludingExtendedAndCompressedRecords(GameMode mode, bool compressed, int padding)
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var source = padding == 0 ? null : "scn LargeScript\r\n;" + new string('a', padding)
            + "\r\nMessageEx \"Reach level 8 with good karma.\"\r\n; Привет";
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FaLoudit.slnx"))) root = root.Parent;
        var directory = Path.Combine(root!.FullName, ".falloutloc", "fixtures", "large-script-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "large-" + Guid.NewGuid().ToString("N") + ".esp");
        try
        {
            File.WriteAllBytes(path, Build(source, compressed));
            using var session = new MutagenPluginBackend(new StrictPluginStringDecoder("en", "ru"))
                .Open(new PluginOpenRequest { Path = path, Mode = mode, LoadOrderIndex = 0, SourceMod = "fixture" });
            var record = Assert.Single(session.EnumerateMajorRecords());
            Assert.Equal("LargeScript", record.EditorId);
            if (source is null)
            {
                Assert.Empty(record.Contents);
                return;
            }
            var content = Assert.Single(record.Contents);
            var carrier = System.Text.Encoding.GetEncoding(1252).GetString(System.Text.Encoding.GetEncoding(1251).GetBytes(source));
            Assert.Equal(new StrictPluginStringDecoder("en", "ru").Decode(carrier).Text, content.Text);
            Assert.Contains("Reach level 8 with good karma.", content.Text);
            Assert.Equal("Fields.SourceCode", content.SemanticPath);
            Assert.Equal(RecordContentSourceKind.EmbeddedScriptSource, content.SourceKind);
        }
        finally { File.Delete(path); }
    }

    private static byte[] Build(string? source, bool compressed)
    {
        var encoding = System.Text.Encoding.GetEncoding(1251);
        byte[] Bytes(Action<BinaryWriter> write)
        {
            using var memory = new MemoryStream();
            using var writer = new BinaryWriter(memory, encoding, true);
            write(writer);
            return memory.ToArray();
        }
        void Sub(BinaryWriter w, string tag, byte[] data)
        {
            if (data.Length > ushort.MaxValue)
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("XXXX")); w.Write((ushort)4); w.Write(data.Length);
            }
            w.Write(System.Text.Encoding.ASCII.GetBytes(tag)); w.Write(data.Length > ushort.MaxValue ? (ushort)0 : (ushort)data.Length); w.Write(data);
        }
        byte[] Record(string tag, uint id, byte[] data, uint flags = 0) => Bytes(w =>
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes(tag)); w.Write(data.Length); w.Write(flags); w.Write(id);
            w.Write(0u); w.Write(0u); w.Write(data);
        });
        var header = Record("TES4", 0, Bytes(w => Sub(w, "HEDR", Bytes(h => { h.Write(1.34f); h.Write(1); h.Write(0x801u); }))));
        var body = Bytes(w =>
        {
            Sub(w, "EDID", encoding.GetBytes("LargeScript\0"));
            Sub(w, "SCHR", new byte[20]);
            Sub(w, "SCDA", new byte[4]);
            if (source is not null) Sub(w, "SCTX", encoding.GetBytes(source));
        });
        if (compressed)
        {
            var original = body;
            using var memory = new MemoryStream();
            using (var zip = new ZLibStream(memory, CompressionMode.Compress, true)) zip.Write(original);
            body = Bytes(w => { w.Write(original.Length); w.Write(memory.ToArray()); });
        }
        var script = Record("SCPT", 0x800, body, compressed ? 0x40000u : 0);
        return Bytes(w =>
        {
            w.Write(header); w.Write(System.Text.Encoding.ASCII.GetBytes("GRUP")); w.Write(script.Length + 24);
            w.Write(System.Text.Encoding.ASCII.GetBytes("SCPT")); w.Write(0); w.Write(0u); w.Write(0u); w.Write(script);
        });
    }
}
