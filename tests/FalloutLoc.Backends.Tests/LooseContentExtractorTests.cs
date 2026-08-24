using System.Text;
using FalloutLoc.Backends.Encoding;
using FalloutLoc.Backends.Loose;
using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Tests;

public sealed class LooseContentExtractorTests
{
    private readonly LooseContentExtractor _extractor = new(
        new StrictPluginStringDecoder("en", "ru"));

    [Fact]
    public void ExtractsQuotedTextualGeckScriptLiteralsWithExecutableLineContext()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = System.Text.Encoding.GetEncoding(1251).GetBytes("""
            ; MessageBoxEx "Комментарий"
            let empty := ""
            MessageBoxEx "Настоящая строка" ; ignored "after comment"
            SetUIString "Inventory/title" "Открыть"
            """);

        var result = _extractor.Extract(
            @"NVSE\user_defined_functions\Example\Display.gek",
            RecordContentSourceKind.LooseScript,
            bytes);

        Assert.Empty(result.Warnings);
        Assert.Equal(3, result.Entries.Count);
        var message = Assert.Single(result.Entries, entry => entry.Text == "Настоящая строка");
        Assert.Equal(3, message.LineNumber);
        Assert.Equal("line[3].literal[1]", message.SemanticPath);
        Assert.Contains("MessageBoxEx", message.Context);
        Assert.Equal(StringEncodingEvidence.TargetCodePageRecovered, message.EncodingEvidence);
        Assert.DoesNotContain(result.Entries, entry => entry.Text.Contains("Комментарий", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractsIniValuesAndExcludesCommentsAndSectionHeaders()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("""
            # ignored = Not runtime
            [Interface]
            Open = "Открыть"
            Number = 42 ; inline comment is not content
            ; Hidden = Ignore me
            """);

        var result = _extractor.Extract(
            @"Config\Example.ini",
            RecordContentSourceKind.IniValue,
            bytes);

        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.Entries.Count);
        var open = Assert.Single(result.Entries, entry => entry.Text == "Открыть");
        Assert.Equal("[Interface].Open", open.SemanticPath);
        Assert.Equal(3, open.LineNumber);
        Assert.Equal("42", Assert.Single(result.Entries, entry => entry.SemanticPath == "[Interface].Number").Text);
        Assert.DoesNotContain(result.Entries, entry => entry.Text.Contains("runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsUnmarkedBinaryFiles()
    {
        var result = _extractor.Extract(
            "binary.ini",
            RecordContentSourceKind.IniValue,
            [0x41, 0x00, 0x42]);

        Assert.Empty(result.Entries);
        Assert.Single(result.Warnings);
    }
}
