namespace FalloutLoc.Backends.Models;

public sealed record LocalizationFieldDefinition(
    string RecordType,
    string SemanticPathPattern,
    string Category,
    string Notes);

public static class LocalizationFieldCatalog
{
    public const string Version = "1";

    public static IReadOnlyList<LocalizationFieldDefinition> SupportedFields { get; } =
    [
        new("*", "Name", "display-name", "Records implementing a Bethesda named aspect."),
        new("*", "Description", "description", "Root translated/string property when present."),
        new("*", "ShortName", "short-name", "Root translated/string property when present."),
        new("*", "Abbreviation", "abbreviation", "Root translated/string property when present."),
        new("*", "ActivationPrompt", "activation-prompt", "Root translated/string property when present."),
        new("*", "VatsAttackName", "vats-attack-name", "Root translated/string property when present."),
        new("*", "DumbResponse", "dialogue-dumb-response", "Root translated/string property when present."),
        new("*", "Prompt", "dialogue-prompt", "Root translated/string property when present."),
        new("GameSettingString", "Data", "game-setting", "String-valued GMST data."),
        new("DialogResponses", "Responses[number=*,occurrence=*].ResponseText", "dialogue-response", "Dialogue response text."),
        new("Quest", "Stages[index=*].LogEntries[*].Entry", "quest-log", "Quest stage log entries."),
        new("Quest", "Objectives[index=*,occurrence=*].Description", "quest-objective", "Quest objective text."),
        new("Terminal", "MenuItems[*].ItemText", "terminal-menu", "Terminal menu labels."),
        new("Terminal", "MenuItems[*].ResultText", "terminal-result", "Terminal result/body text."),
        new("Message", "MenuButtons[*].Text", "message-button", "Message box buttons."),
        new("Note", "Data.Text", "note-text", "Standard note body text."),
        new("Perk", "Effects[type=*,rank=*,priority=*,entryPoint=*,occurrence=*].ButtonLabel", "perk-activation-button", "Activate-choice perk button labels."),
        new("Faction", "Ranks[number=*].Name.Male", "faction-rank", "Male faction rank title."),
        new("Faction", "Ranks[number=*].Name.Female", "faction-rank", "Female faction rank title."),
        new("BodyPartData", "Parts[actorValue=*,type=*,occurrence=*].Name", "body-part-name", "Body part names."),
        new("PlacedObject", "MapMarker.Name", "map-marker", "Map marker display name."),
        new("PlacedObject", "AudioData.LocationName", "radio-location", "Radio/audio location name."),
        new("Region", "MapName.Map", "region-map-name", "Region map name."),
    ];

    public static IReadOnlySet<string> SpecializedLocalizedRecordTypes { get; } =
        new HashSet<string>(SupportedFields
            .Where(field => field.RecordType != "*")
            .Select(field => field.RecordType), StringComparer.Ordinal);

    public static IReadOnlySet<string> AuditedNonLocalizedRecordTypes { get; } = new HashSet<string>(
    [
        "AcousticSpace", "AddonNode", "AnimatedObject", "CameraPath", "CameraShot", "Climate",
        "CombatStyle", "Debris", "DefaultObjectManager", "DehydrationStage", "EffectShader",
        "EncounterZone", "FormList", "GameSettingFloat", "GameSettingInt", "GlobalFloat",
        "GlobalInt", "GlobalShort", "Grass", "HungerStage", "IdleAnimation", "IdleMarker",
        "ImageSpace", "ImageSpaceAdapter", "Impact", "ImpactDataSet", "Landscape",
        "LandscapeTexture", "LeveledCreature", "LeveledItem", "LeveledNpc", "LightingTemplate",
        "LoadScreenType", "MenuIcon", "MusicType", "NavigationMesh", "NavigationMeshInfoMap",
        "Package", "PlaceableWater", "RadiationStage", "Ragdoll", "SleepDeprivationStage",
        "Sound", "Static", "StaticCollection", "TextureSet", "Tree", "VoiceType", "Weather",
    ], StringComparer.Ordinal);
}
