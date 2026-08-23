using System.Text;
using FalloutLoc.Backends.Encoding;
using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Tests;

public sealed class StrictPluginStringDecoderTests
{
    static StrictPluginStringDecoderTests()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void RecoversWindows1251CyrillicFromMutagenCp1252Value()
    {
        const string expected = "Самовыстреливающий дробовик";
        var cp1251 = System.Text.Encoding.GetEncoding(1251);
        var cp1252 = System.Text.Encoding.GetEncoding(1252);
        var backendValue = cp1252.GetString(cp1251.GetBytes(expected));
        var decoder = new StrictPluginStringDecoder();

        var actual = decoder.Decode(backendValue);

        Assert.Equal(expected, actual.Text);
        Assert.Equal(TextLanguageKind.Russian, actual.Language);
        Assert.Equal(StringEncodingEvidence.Windows1251Recovered, actual.EncodingEvidence);
        Assert.False(actual.IsAmbiguous);
        Assert.NotNull(actual.RecoveredBytesSha256);
    }

    [Fact]
    public void PreservesAsciiWithoutClaimingAnEncoding()
    {
        var actual = new StrictPluginStringDecoder().Decode("Tactical Helmet");

        Assert.Equal("Tactical Helmet", actual.Text);
        Assert.Equal(TextLanguageKind.English, actual.Language);
        Assert.Equal(StringEncodingEvidence.Ascii, actual.EncodingEvidence);
        Assert.False(actual.IsAmbiguous);
    }

    [Fact]
    public void MarksNonCp1252UnicodeAsUnrecoverableInsteadOfReplacingIt()
    {
        var actual = new StrictPluginStringDecoder().Decode("漢字");

        Assert.Equal("漢字", actual.Text);
        Assert.Equal(StringEncodingEvidence.UnrecoverableUnicode, actual.EncodingEvidence);
        Assert.True(actual.IsAmbiguous);
        Assert.Null(actual.RecoveredBytesSha256);
    }

    [Fact]
    public void AllCp1252BytesRoundTripStrictly()
    {
        new StrictPluginStringDecoder().VerifyByteRecoveryInvariant();
    }

    [Fact]
    public void PluginClassifierDoesNotGuessFromAsciiOrAmbiguousPunctuation()
    {
        var decoder = new StrictPluginStringDecoder();
        var classifier = new PluginEncodingClassifier();
        var ascii = Occurrence(decoder.Decode("Only ASCII"));
        var ambiguous = Occurrence(decoder.Decode("90°"));

        Assert.Equal(
            PluginEncodingClass.AsciiOnlyOrNoUserText,
            classifier.Classify([ascii]).Classification);
        Assert.Equal(
            PluginEncodingClass.SingleByteAmbiguous,
            classifier.Classify([ascii, ambiguous]).Classification);
    }

    private static RecordStringOccurrence Occurrence(DecodedString decoded) => new()
    {
        SemanticPath = "Name",
        Category = "display-name",
        Text = decoded.Text,
        Language = decoded.Language,
        EncodingEvidence = decoded.EncodingEvidence,
        RecoveredBytesSha256 = decoded.RecoveredBytesSha256,
        Ambiguous = decoded.IsAmbiguous,
    };
}
