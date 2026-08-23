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
        var decoder = new StrictPluginStringDecoder("en", "ru");

        var actual = decoder.Decode(backendValue);

        Assert.Equal(expected, actual.Text);
        Assert.Equal(TextLanguageKind.Russian, actual.Language);
        Assert.Equal(StringEncodingEvidence.Windows1251Recovered, actual.EncodingEvidence);
        Assert.False(actual.IsAmbiguous);
        Assert.NotNull(actual.RecoveredBytesSha256);
    }

    [Theory]
    [InlineData("pl", 1250, "Zażółć gęślą jaźń")]
    [InlineData("de", 1252, "Schließen")]
    [InlineData("fr", 1252, "Équipement amélioré")]
    [InlineData("es", 1252, "Cañón automático")]
    [InlineData("tr", 1254, "Gelişmiş zırh")]
    public void RecoversConfiguredEuropeanTargetCodePage(string target, int codePage, string expected)
    {
        var targetEncoding = System.Text.Encoding.GetEncoding(codePage);
        var cp1252 = System.Text.Encoding.GetEncoding(1252);
        var backendValue = cp1252.GetString(targetEncoding.GetBytes(expected));

        var actual = new StrictPluginStringDecoder("en", target).Decode(backendValue);

        Assert.Equal(expected, actual.Text);
        Assert.Equal(TextLanguageKind.Target, actual.Language);
        Assert.Equal(StringEncodingEvidence.TargetCodePageRecovered, actual.EncodingEvidence);
        Assert.False(actual.IsAmbiguous);
    }

    [Fact]
    public void PreservesDirectUnicodeUkrainianTarget()
    {
        var actual = new StrictPluginStringDecoder("en", "uk").Decode("Поліпшена зброя");

        Assert.Equal("Поліпшена зброя", actual.Text);
        Assert.Equal(TextLanguageKind.Target, actual.Language);
        Assert.Equal(StringEncodingEvidence.UnicodeTarget, actual.EncodingEvidence);
        Assert.False(actual.IsAmbiguous);
    }

    [Fact]
    public void PreservesAsciiWithoutClaimingAnEncoding()
    {
        var actual = new StrictPluginStringDecoder("en", "ru").Decode("Tactical Helmet");

        Assert.Equal("Tactical Helmet", actual.Text);
        Assert.Equal(TextLanguageKind.English, actual.Language);
        Assert.Equal(StringEncodingEvidence.Ascii, actual.EncodingEvidence);
        Assert.False(actual.IsAmbiguous);
    }

    [Fact]
    public void DoesNotTurnWesternSourceAccentIntoOneCyrillicLetter()
    {
        var actual = new StrictPluginStringDecoder("en", "ru").Decode("café");

        Assert.Equal("café", actual.Text);
        Assert.Equal(TextLanguageKind.Source, actual.Language);
        Assert.NotEqual(StringEncodingEvidence.TargetCodePageRecovered, actual.EncodingEvidence);
    }

    [Fact]
    public void MarksNonCp1252UnicodeAsUnrecoverableInsteadOfReplacingIt()
    {
        var actual = new StrictPluginStringDecoder("en", "ru").Decode("漢字");

        Assert.Equal("漢字", actual.Text);
        Assert.Equal(StringEncodingEvidence.UnrecoverableUnicode, actual.EncodingEvidence);
        Assert.True(actual.IsAmbiguous);
        Assert.Null(actual.RecoveredBytesSha256);
    }

    [Fact]
    public void AllCp1252BytesRoundTripStrictly()
    {
        new StrictPluginStringDecoder("en", "ru").VerifyByteRecoveryInvariant();
    }

    [Fact]
    public void PluginClassifierDoesNotGuessFromAsciiOrAmbiguousPunctuation()
    {
        var decoder = new StrictPluginStringDecoder("en", "ru");
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
