using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;
using FalloutLoc.Core.Configuration;

namespace FalloutLoc.Backends.Encoding;

public sealed partial class StrictPluginStringDecoder : IPluginStringDecoder
{
    private readonly LocalizationLanguageProfile _source;
    private readonly LocalizationLanguageProfile _target;
    private readonly System.Text.Encoding _sourceEncoding;
    private readonly System.Text.Encoding _targetEncoding;
    private readonly System.Text.Encoding _windows1252;
    private readonly UTF8Encoding _strictUtf8 = new(false, true);

    public StrictPluginStringDecoder(string sourceLanguage, string targetLanguage)
    {
        var pair = LocalizationLanguages.ValidatePair(sourceLanguage, targetLanguage);
        _source = LocalizationLanguages.Get(pair.Source);
        _target = LocalizationLanguages.Get(pair.Target);
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _sourceEncoding = StrictEncoding(_source.WindowsCodePage);
        _targetEncoding = StrictEncoding(_target.WindowsCodePage);
        _windows1252 = StrictEncoding(1252);
        VerifyByteRecoveryInvariant();
    }

    public string SourceLanguage => _source.Tag;

    public string TargetLanguage => _target.Tag;

    public DecodedString Decode(string? backendValue)
    {
        if (string.IsNullOrEmpty(backendValue))
        {
            return Create(backendValue, TextLanguageKind.Empty, StringEncodingEvidence.None, null);
        }

        if (backendValue.All(character => character <= 0x7F))
        {
            return Create(backendValue, ClassifyLanguage(backendValue), StringEncodingEvidence.Ascii, null);
        }

        var directRole = ClassifyLanguage(backendValue);
        byte[] bytes;
        try
        {
            bytes = _windows1252.GetBytes(backendValue);
        }
        catch (EncoderFallbackException)
        {
            return directRole is TextLanguageKind.Target or TextLanguageKind.Source
                ? Create(
                    backendValue,
                    directRole,
                    directRole == TextLanguageKind.Target
                        ? StringEncodingEvidence.UnicodeTarget
                        : StringEncodingEvidence.SourceCodePageRecovered,
                    null)
                : Create(backendValue, directRole, StringEncodingEvidence.UnrecoverableUnicode, null);
        }

        var byteHash = Convert.ToHexString(SHA256.HashData(bytes));
        try
        {
            var utf8 = _strictUtf8.GetString(bytes);
            var utf8Role = ClassifyLanguage(utf8);
            if (utf8Role is TextLanguageKind.Target or TextLanguageKind.Source)
            {
                return Create(utf8, utf8Role, StringEncodingEvidence.Utf8Recovered, byteHash);
            }
        }
        catch (DecoderFallbackException)
        {
            // Continue with the configured single-byte language profiles.
        }

        var targetCandidate = _targetEncoding.GetString(bytes);
        if (ClassifyLanguage(targetCandidate) == TextLanguageKind.Target)
        {
            return Create(
                targetCandidate,
                TextLanguageKind.Target,
                StringEncodingEvidence.TargetCodePageRecovered,
                byteHash);
        }

        var sourceCandidate = _sourceEncoding.GetString(bytes);
        if (ClassifyLanguage(sourceCandidate) == TextLanguageKind.Source)
        {
            return Create(
                sourceCandidate,
                TextLanguageKind.Source,
                StringEncodingEvidence.SourceCodePageRecovered,
                byteHash);
        }

        if (directRole is TextLanguageKind.Target or TextLanguageKind.Source)
        {
            return Create(
                backendValue,
                directRole,
                directRole == TextLanguageKind.Target
                    ? StringEncodingEvidence.UnicodeTarget
                    : StringEncodingEvidence.SourceCodePageRecovered,
                byteHash);
        }

        return Create(
            targetCandidate,
            ClassifyLanguage(targetCandidate),
            StringEncodingEvidence.SingleByteAmbiguous,
            byteHash);
    }

    public void VerifyByteRecoveryInvariant()
    {
        var original = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var roundTrip = _windows1252.GetBytes(_windows1252.GetString(original));
        if (!original.AsSpan().SequenceEqual(roundTrip))
        {
            throw new InvalidOperationException(
                "The active CP1252 implementation does not round-trip all byte values. Embedded string recovery is unsafe.");
        }
    }

    private TextLanguageKind ClassifyLanguage(string value)
    {
        if (value.Length == 0)
        {
            return TextLanguageKind.Empty;
        }

        var cyrillicCount = value.Count(character => character is >= '\u0400' and <= '\u04FF');
        var latinCount = value.Count(character => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '\u00C0' and <= '\u024F');

        if (ContainsDistinguishingCharacters(value, _target)
            && (_target.Script == _source.Script || ScriptCount(_target.Script) >= ScriptCount(_source.Script)))
        {
            return TextLanguageKind.Target;
        }

        if (ContainsDistinguishingCharacters(value, _source)
            && (_target.Script == _source.Script || ScriptCount(_source.Script) >= ScriptCount(_target.Script)))
        {
            return TextLanguageKind.Source;
        }

        if (_target.Script != _source.Script)
        {
            if (cyrillicCount > 0 && cyrillicCount >= latinCount)
            {
                return _target.Script == LocalizationScript.Cyrillic
                    ? TextLanguageKind.Target
                    : TextLanguageKind.Source;
            }

            if (latinCount >= 2 && latinCount >= cyrillicCount)
            {
                return _target.Script == LocalizationScript.Latin
                    ? TextLanguageKind.Target
                    : TextLanguageKind.Source;
            }
        }

        if (_source.Script == LocalizationScript.Latin && LatinWordRegex().IsMatch(value))
        {
            return TextLanguageKind.Source;
        }

        return TextLanguageKind.Other;

        int ScriptCount(LocalizationScript script) => script == LocalizationScript.Cyrillic
            ? cyrillicCount
            : latinCount;
    }

    private static bool ContainsDistinguishingCharacters(string value, LocalizationLanguageProfile profile) =>
        profile.DistinguishingCharacters.Length > 0
        && value.IndexOfAny(profile.DistinguishingCharacters.ToCharArray()) >= 0;

    private static System.Text.Encoding StrictEncoding(int codePage) => System.Text.Encoding.GetEncoding(
        codePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    private static DecodedString Create(
        string? text,
        TextLanguageKind language,
        StringEncodingEvidence evidence,
        string? byteHash) => new()
        {
            Text = text,
            Language = language,
            EncodingEvidence = evidence,
            RecoveredBytesSha256 = byteHash,
        };

    [GeneratedRegex("[A-Za-z]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex LatinWordRegex();
}
