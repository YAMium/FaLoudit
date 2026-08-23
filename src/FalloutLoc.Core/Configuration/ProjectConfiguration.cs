namespace FalloutLoc.Core.Configuration;

public sealed record ProjectConfiguration
{
    public int SchemaVersion { get; init; } = 2;

    public string? SourceLanguage { get; init; }

    public string? TargetLanguage { get; init; }

    public required GameMode Mode { get; init; }

    public required string Mo2Root { get; init; }

    public required string ModsRoot { get; init; }

    public required string ProfilesRoot { get; init; }

    public required string ProfileName { get; init; }

    public required string OverwriteRoot { get; init; }

    public required string GameRoot { get; init; }

    public required string DataRoot { get; init; }

    public (string Source, string Target) RequireLanguagePair()
    {
        if (string.IsNullOrWhiteSpace(SourceLanguage) || string.IsNullOrWhiteSpace(TargetLanguage))
        {
            throw new InvalidOperationException(
                "Localization languages are not configured. Run 'faloudit configure <mo2-root> " +
                "--source-language <tag> --target-language <tag>'.");
        }

        return LocalizationLanguages.ValidatePair(SourceLanguage, TargetLanguage);
    }
}
