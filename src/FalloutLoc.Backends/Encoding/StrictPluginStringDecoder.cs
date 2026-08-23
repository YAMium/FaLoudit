using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;

namespace FalloutLoc.Backends.Encoding;

public sealed partial class StrictPluginStringDecoder : IPluginStringDecoder
{
    private readonly System.Text.Encoding _windows1251;
    private readonly System.Text.Encoding _windows1252;
    private readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public StrictPluginStringDecoder()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _windows1251 = System.Text.Encoding.GetEncoding(
            1251,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
        _windows1252 = System.Text.Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
        VerifyByteRecoveryInvariant();
    }

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

        if (ContainsCyrillic(backendValue))
        {
            return Create(
                backendValue,
                TextLanguageKind.Russian,
                StringEncodingEvidence.UnicodeCyrillic,
                null);
        }

        byte[] bytes;
        try
        {
            bytes = _windows1252.GetBytes(backendValue);
        }
        catch (EncoderFallbackException)
        {
            return Create(
                backendValue,
                ClassifyLanguage(backendValue),
                StringEncodingEvidence.UnrecoverableUnicode,
                null);
        }

        var byteHash = Convert.ToHexString(SHA256.HashData(bytes));
        try
        {
            var utf8 = _strictUtf8.GetString(bytes);
            if (ContainsCyrillic(utf8))
            {
                return Create(
                    utf8,
                    TextLanguageKind.Russian,
                    StringEncodingEvidence.Utf8Recovered,
                    byteHash);
            }
        }
        catch (DecoderFallbackException)
        {
            // The same recovered bytes are tested as Windows-1251 below.
        }

        var windows1251 = _windows1251.GetString(bytes);
        if (ContainsCyrillic(windows1251))
        {
            return Create(
                windows1251,
                TextLanguageKind.Russian,
                StringEncodingEvidence.Windows1251Recovered,
                byteHash);
        }

        return Create(
            windows1251,
            ClassifyLanguage(windows1251),
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

    private static TextLanguageKind ClassifyLanguage(string value)
    {
        if (value.Length == 0)
        {
            return TextLanguageKind.Empty;
        }

        if (ContainsCyrillic(value))
        {
            return TextLanguageKind.Russian;
        }

        return EnglishWordRegex().IsMatch(value) ? TextLanguageKind.English : TextLanguageKind.Other;
    }

    private static bool ContainsCyrillic(string value) => CyrillicRegex().IsMatch(value);

    [GeneratedRegex("[\\u0400-\\u04FF]", RegexOptions.CultureInvariant)]
    private static partial Regex CyrillicRegex();

    [GeneratedRegex("[A-Za-z]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishWordRegex();
}
