using System.Text;

namespace FalloutLoc.Index;

internal static class TextNormalizer
{
    public static string? Normalize(string? value) => value?.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
}
