using System.Text.RegularExpressions;

namespace FalloutLoc.Core.Configuration;

public sealed record LocalizationLanguageProfile(
    string Tag,
    string EnglishName,
    int WindowsCodePage,
    LocalizationScript Script,
    string DistinguishingCharacters);

public enum LocalizationScript
{
    Latin,
    Cyrillic,
}

public static partial class LocalizationLanguages
{
    private static readonly IReadOnlyDictionary<string, LocalizationLanguageProfile> Profiles =
        new Dictionary<string, LocalizationLanguageProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("en", "English", 1252, LocalizationScript.Latin, string.Empty),
            ["de"] = new("de", "German", 1252, LocalizationScript.Latin, "ÄÖÜäöüßẞ"),
            ["fr"] = new("fr", "French", 1252, LocalizationScript.Latin, "ÀÂÆÇÉÈÊËÎÏÔŒÙÛÜŸàâæçéèêëîïôœùûüÿ"),
            ["es"] = new("es", "Spanish", 1252, LocalizationScript.Latin, "ÁÉÍÑÓÚÜ¿¡áéíñóúü"),
            ["it"] = new("it", "Italian", 1252, LocalizationScript.Latin, "ÀÈÉÌÍÎÒÓÙÚàèéìíîòóùú"),
            ["pt"] = new("pt", "Portuguese", 1252, LocalizationScript.Latin, "ÁÂÃÀÇÉÊÍÓÔÕÚÜáâãàçéêíóôõúü"),
            ["pl"] = new("pl", "Polish", 1250, LocalizationScript.Latin, "ĄĆĘŁŃÓŚŹŻąćęłńóśźż"),
            ["cs"] = new("cs", "Czech", 1250, LocalizationScript.Latin, "ÁČĎÉĚÍŇÓŘŠŤÚŮÝŽáčďéěíňóřšťúůýž"),
            ["sk"] = new("sk", "Slovak", 1250, LocalizationScript.Latin, "ÁÄČĎÉÍĹĽŇÓÔŔŠŤÚÝŽáäčďéíĺľňóôŕšťúýž"),
            ["hu"] = new("hu", "Hungarian", 1250, LocalizationScript.Latin, "ÁÉÍÓÖŐÚÜŰáéíóöőúüű"),
            ["tr"] = new("tr", "Turkish", 1254, LocalizationScript.Latin, "ÇĞİÖŞÜçğıöşü"),
            ["ru"] = new("ru", "Russian", 1251, LocalizationScript.Cyrillic, "ЁёЫыЭэЪъ"),
            ["uk"] = new("uk", "Ukrainian", 1251, LocalizationScript.Cyrillic, "ЄІЇҐєіїґ"),
            ["be"] = new("be", "Belarusian", 1251, LocalizationScript.Cyrillic, "ЎўІі"),
            ["bg"] = new("bg", "Bulgarian", 1251, LocalizationScript.Cyrillic, "ЪъЩщ"),
        };

    public static IReadOnlyCollection<LocalizationLanguageProfile> Supported => Profiles.Values.ToArray();

    public static string NormalizeTag(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().Replace('_', '-').ToLowerInvariant();
        if (!LanguageTagRegex().IsMatch(normalized))
        {
            throw new ArgumentException($"Invalid language tag: {value}.", nameof(value));
        }

        var primary = normalized.Split('-', 2)[0];
        if (!Profiles.ContainsKey(primary))
        {
            throw new ArgumentException(
                $"Unsupported localization language '{value}'. Supported tags: {string.Join(", ", Profiles.Keys.Order())}.",
                nameof(value));
        }

        return primary;
    }

    public static LocalizationLanguageProfile Get(string value) => Profiles[NormalizeTag(value)];

    public static (string Source, string Target) ValidatePair(string sourceLanguage, string targetLanguage)
    {
        var source = NormalizeTag(sourceLanguage);
        var target = NormalizeTag(targetLanguage);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("sourceLanguage and targetLanguage must be different.");
        }

        return (source, target);
    }

    [GeneratedRegex("^[a-zA-Z]{2,3}(?:[-_][a-zA-Z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagRegex();
}
