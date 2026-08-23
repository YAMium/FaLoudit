using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Core.Tests;

public sealed class LocalizationLanguagesTests
{
    [Theory]
    [InlineData("EN-us", "en")]
    [InlineData("pt_BR", "pt")]
    [InlineData("RU", "ru")]
    public void NormalizesSupportedBcp47StyleTags(string value, string expected) =>
        Assert.Equal(expected, LocalizationLanguages.NormalizeTag(value));

    [Fact]
    public void RejectsUnsupportedLanguageInsteadOfGuessing() =>
        Assert.Throws<ArgumentException>(() => LocalizationLanguages.NormalizeTag("ja"));

    [Fact]
    public void RejectsEqualSourceAndTarget() =>
        Assert.Throws<ArgumentException>(() => LocalizationLanguages.ValidatePair("en", "en-US"));

    [Fact]
    public void LegacyConfigurationRequiresExplicitLanguageMigration()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Mode = GameMode.TaleOfTwoWastelands,
            Mo2Root = "M",
            ModsRoot = "M/mods",
            ProfilesRoot = "M/profiles",
            ProfileName = "A",
            OverwriteRoot = "M/overwrite",
            GameRoot = "G",
            DataRoot = "G/Data",
        };

        Assert.Throws<InvalidOperationException>(() => configuration.RequireLanguagePair());
    }
}
