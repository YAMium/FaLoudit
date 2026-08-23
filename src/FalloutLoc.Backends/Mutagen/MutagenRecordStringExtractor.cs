using System.Collections;
using System.Reflection;
using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;

namespace FalloutLoc.Backends.Mutagen;

internal sealed class MutagenRecordStringExtractor : IRecordStringExtractor<IMajorRecordGetter>
{
    public RecordStringExtractionResult Extract(IMajorRecordGetter record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var typeName = LogicalTypeName(record);
        RawRecordString[] strings;
        try
        {
            strings = ExtractStrings(record).ToArray();
        }
        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
        {
            return new RecordStringExtractionResult
            {
                Strings = [],
                Status = RecordParseStatus.PartiallyParsed,
                Warnings = [$"String extraction failed: {exception.GetType().Name}: {exception.Message}"],
            };
        }

        IReadOnlyList<string> warnings;
        try
        {
            warnings = AuditExpectedFields(record, typeName);
        }
        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
        {
            warnings = [$"Coverage audit failed: {exception.GetType().Name}: {exception.Message}"];
        }

        if (warnings.Count > 0)
        {
            return new RecordStringExtractionResult
            {
                Strings = strings,
                Status = RecordParseStatus.PartiallyParsed,
                Warnings = warnings,
            };
        }

        if (strings.Length > 0 || LocalizationFieldCatalog.SpecializedLocalizedRecordTypes.Contains(typeName))
        {
            return new RecordStringExtractionResult
            {
                Strings = strings,
                Status = RecordParseStatus.Parsed,
                Warnings = [],
            };
        }

        if (LocalizationFieldCatalog.AuditedNonLocalizedRecordTypes.Contains(typeName))
        {
            return new RecordStringExtractionResult
            {
                Strings = [],
                Status = RecordParseStatus.NotApplicable,
                Warnings = [],
            };
        }

        var warning = typeName == "Script"
            ? "Compiled script string literals are not extracted by the current read-only backend."
            : $"Record type {typeName} has no audited localization field contract.";
        return new RecordStringExtractionResult
        {
            Strings = [],
            Status = RecordParseStatus.Unverified,
            Warnings = [warning],
        };
    }

    private static IEnumerable<RawRecordString> ExtractStrings(IMajorRecordGetter record)
    {
        if (TryGetName(record, out var name))
        {
            yield return new RawRecordString("Name", "display-name", name);
        }

        foreach (var property in new[]
                 {
                     "Description", "ShortName", "Abbreviation", "ActivationPrompt",
                     "VatsAttackName", "DumbResponse", "Prompt",
                 })
        {
            if (TryGetStringProperty(record, property, out var value))
            {
                yield return new RawRecordString(property, RootCategory(property), value);
            }
        }

        var typeName = LogicalTypeName(record);
        if (typeName == "GameSettingString" && TryGetStringProperty(record, "Data", out var gameSetting))
        {
            yield return new RawRecordString("Data", "game-setting", gameSetting);
        }

        if (typeName == "DialogResponses")
        {
            var occurrenceByNumber = new Dictionary<int, int>();
            foreach (var response in GetItems(record, "Responses"))
            {
                var number = GetInt(GetProperty(response, "ResponseData"), "ResponseNumber") ?? -1;
                occurrenceByNumber.TryGetValue(number, out var occurrence);
                occurrenceByNumber[number] = occurrence + 1;
                if (TryGetStringProperty(response, "ResponseText", out var responseText))
                {
                    yield return new RawRecordString(
                        $"Responses[number={number},occurrence={occurrence}].ResponseText",
                        "dialogue-response",
                        responseText);
                }
            }
        }

        if (typeName == "Quest")
        {
            foreach (var stage in GetItems(record, "Stages"))
            {
                var stageIndex = GetInt(stage, "Index") ?? -1;
                var entryIndex = 0;
                foreach (var entry in GetItems(stage, "LogEntries"))
                {
                    if (TryGetStringProperty(entry, "Entry", out var entryText))
                    {
                        yield return new RawRecordString(
                            $"Stages[index={stageIndex}].LogEntries[{entryIndex}].Entry",
                            "quest-log",
                            entryText);
                    }

                    entryIndex++;
                }
            }

            var objectiveOccurrence = new Dictionary<int, int>();
            foreach (var objective in GetItems(record, "Objectives"))
            {
                var objectiveIndex = GetInt(objective, "Index") ?? -1;
                objectiveOccurrence.TryGetValue(objectiveIndex, out var occurrence);
                objectiveOccurrence[objectiveIndex] = occurrence + 1;
                if (TryGetStringProperty(objective, "Description", out var objectiveText))
                {
                    yield return new RawRecordString(
                        $"Objectives[index={objectiveIndex},occurrence={occurrence}].Description",
                        "quest-objective",
                        objectiveText);
                }
            }
        }

        if (typeName == "Terminal")
        {
            var index = 0;
            foreach (var menuItem in GetItems(record, "MenuItems"))
            {
                if (TryGetStringProperty(menuItem, "ItemText", out var itemText))
                {
                    yield return new RawRecordString($"MenuItems[{index}].ItemText", "terminal-menu", itemText);
                }

                if (TryGetStringProperty(menuItem, "ResultText", out var resultText))
                {
                    yield return new RawRecordString($"MenuItems[{index}].ResultText", "terminal-result", resultText);
                }

                index++;
            }
        }

        if (typeName == "Message")
        {
            var index = 0;
            foreach (var button in GetItems(record, "MenuButtons"))
            {
                if (TryGetStringProperty(button, "Text", out var text))
                {
                    yield return new RawRecordString($"MenuButtons[{index}].Text", "message-button", text);
                }

                index++;
            }
        }

        if (typeName == "Note")
        {
            var data = GetProperty(record, "Data");
            if (data is not null
                && LogicalTypeName(data) == "NoteStandard"
                && TryGetStringProperty(data, "Text", out var text))
            {
                yield return new RawRecordString("Data.Text", "note-text", text);
            }
        }

        if (typeName == "Perk")
        {
            var effectOccurrence = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var effect in GetItems(record, "Effects"))
            {
                if (LogicalTypeName(effect) != "PerkEntryPointAddActivateChoice"
                    || !TryGetStringProperty(effect, "ButtonLabel", out var buttonLabel))
                {
                    continue;
                }

                var identity =
                    $"type={LogicalTypeName(effect)},rank={GetInt(effect, "Rank") ?? -1},priority={GetInt(effect, "Priority") ?? -1},entryPoint={GetProperty(effect, "EntryPoint")}";
                effectOccurrence.TryGetValue(identity, out var occurrence);
                effectOccurrence[identity] = occurrence + 1;
                yield return new RawRecordString(
                    $"Effects[{identity},occurrence={occurrence}].ButtonLabel",
                    "perk-activation-button",
                    buttonLabel);
            }
        }

        if (typeName == "Faction")
        {
            foreach (var rank in GetItems(record, "Ranks"))
            {
                var rankNumber = GetInt(rank, "RankNumber") ?? -1;
                var genderedName = GetProperty(rank, "Name");
                if (genderedName is null)
                {
                    continue;
                }

                if (TryGetStringProperty(genderedName, "Male", out var male))
                {
                    yield return new RawRecordString(
                        $"Ranks[number={rankNumber}].Name.Male",
                        "faction-rank",
                        male);
                }

                if (TryGetStringProperty(genderedName, "Female", out var female))
                {
                    yield return new RawRecordString(
                        $"Ranks[number={rankNumber}].Name.Female",
                        "faction-rank",
                        female);
                }
            }
        }

        if (typeName == "BodyPartData")
        {
            var partOccurrence = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var part in GetItems(record, "Parts"))
            {
                if (!TryGetStringProperty(part, "Name", out var partName))
                {
                    continue;
                }

                var identity = $"actorValue={GetProperty(part, "ActorValue")},type={GetProperty(part, "Type")}";
                partOccurrence.TryGetValue(identity, out var occurrence);
                partOccurrence[identity] = occurrence + 1;
                yield return new RawRecordString(
                    $"Parts[{identity},occurrence={occurrence}].Name",
                    "body-part-name",
                    partName);
            }
        }

        if (typeName == "PlacedObject")
        {
            var mapMarker = GetProperty(record, "MapMarker");
            if (mapMarker is not null && TryGetStringProperty(mapMarker, "Name", out var markerName))
            {
                yield return new RawRecordString("MapMarker.Name", "map-marker", markerName);
            }

            var audioData = GetProperty(record, "AudioData");
            if (audioData is not null && TryGetStringProperty(audioData, "LocationName", out var locationName))
            {
                yield return new RawRecordString("AudioData.LocationName", "radio-location", locationName);
            }
        }

        if (typeName == "Region")
        {
            var mapName = GetProperty(record, "MapName");
            if (mapName is not null && TryGetStringProperty(mapName, "Map", out var map))
            {
                yield return new RawRecordString("MapName.Map", "region-map-name", map);
            }
        }
    }

    private static string RootCategory(string property) => property switch
    {
        "Description" => "description",
        "ShortName" => "short-name",
        "Abbreviation" => "abbreviation",
        "ActivationPrompt" => "activation-prompt",
        "VatsAttackName" => "vats-attack-name",
        "DumbResponse" => "dialogue-dumb-response",
        "Prompt" => "dialogue-prompt",
        _ => "other",
    };

    private static IReadOnlyList<string> AuditExpectedFields(IMajorRecordGetter record, string typeName)
    {
        var warnings = new List<string>();
        foreach (var property in new[]
                 {
                     "Description", "ShortName", "Abbreviation", "ActivationPrompt",
                     "VatsAttackName", "DumbResponse", "Prompt",
                 })
        {
            AuditStringProperty(record, property, property, warnings);
        }

        if (typeName == "GameSettingString")
        {
            AuditStringProperty(record, "Data", "Data", warnings);
        }

        if (typeName == "DialogResponses")
        {
            foreach (var response in GetItems(record, "Responses"))
            {
                AuditStringProperty(response, "ResponseText", "Responses[*].ResponseText", warnings);
            }
        }

        if (typeName == "Quest")
        {
            foreach (var stage in GetItems(record, "Stages"))
            {
                foreach (var entry in GetItems(stage, "LogEntries"))
                {
                    AuditStringProperty(entry, "Entry", "Stages[*].LogEntries[*].Entry", warnings);
                }
            }

            foreach (var objective in GetItems(record, "Objectives"))
            {
                AuditStringProperty(objective, "Description", "Objectives[*].Description", warnings);
            }
        }

        if (typeName == "Terminal")
        {
            foreach (var menuItem in GetItems(record, "MenuItems"))
            {
                AuditStringProperty(menuItem, "ItemText", "MenuItems[*].ItemText", warnings);
                AuditStringProperty(menuItem, "ResultText", "MenuItems[*].ResultText", warnings);
            }
        }

        if (typeName == "Message")
        {
            foreach (var button in GetItems(record, "MenuButtons"))
            {
                AuditStringProperty(button, "Text", "MenuButtons[*].Text", warnings);
            }
        }

        if (typeName == "Note" && GetProperty(record, "Data") is { } noteData
            && LogicalTypeName(noteData) == "NoteStandard")
        {
            AuditStringProperty(noteData, "Text", "Data.Text", warnings);
        }

        if (typeName == "Perk")
        {
            foreach (var effect in GetItems(record, "Effects").Where(effect =>
                         LogicalTypeName(effect) == "PerkEntryPointAddActivateChoice"))
            {
                AuditStringProperty(effect, "ButtonLabel", "Effects[*].ButtonLabel", warnings);
            }
        }

        if (typeName == "Faction")
        {
            foreach (var rank in GetItems(record, "Ranks"))
            {
                if (GetProperty(rank, "Name") is not { } genderedName)
                {
                    continue;
                }

                AuditStringProperty(genderedName, "Male", "Ranks[*].Name.Male", warnings);
                AuditStringProperty(genderedName, "Female", "Ranks[*].Name.Female", warnings);
            }
        }

        if (typeName == "BodyPartData")
        {
            foreach (var part in GetItems(record, "Parts"))
            {
                AuditStringProperty(part, "Name", "Parts[*].Name", warnings);
            }
        }

        if (typeName == "PlacedObject")
        {
            if (GetProperty(record, "MapMarker") is { } mapMarker)
            {
                AuditStringProperty(mapMarker, "Name", "MapMarker.Name", warnings);
            }

            if (GetProperty(record, "AudioData") is { } audioData)
            {
                AuditStringProperty(audioData, "LocationName", "AudioData.LocationName", warnings);
            }
        }

        if (typeName == "Region" && GetProperty(record, "MapName") is { } mapName)
        {
            AuditStringProperty(mapName, "Map", "MapName.Map", warnings);
        }

        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AuditStringProperty(
        object target,
        string propertyName,
        string semanticPath,
        ICollection<string> warnings)
    {
        if (target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) is null)
        {
            return;
        }

        if (!TryGetStringProperty(target, propertyName, out _))
        {
            warnings.Add($"Expected localization field {semanticPath} exists but its value type is unsupported.");
        }
    }

    private static bool TryGetName(IMajorRecordGetter record, out string? name)
    {
        if (record is ITranslatedNamedRequiredGetter translatedRequired)
        {
            name = translatedRequired.Name.String;
            return true;
        }

        if (record is ITranslatedNamedGetter translated)
        {
            name = translated.Name?.String;
            return true;
        }

        if (record is INamedRequiredGetter required)
        {
            name = required.Name;
            return true;
        }

        if (record is INamedGetter named)
        {
            name = named.Name;
            return true;
        }

        name = null;
        return false;
    }

    private static bool TryGetStringProperty(object target, string propertyName, out string? value)
    {
        var raw = GetProperty(target, propertyName);
        if (raw is null)
        {
            value = null;
            return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) is not null;
        }

        if (raw is string text)
        {
            value = text;
            return true;
        }

        var stringProperty = raw.GetType().GetProperty("String", BindingFlags.Public | BindingFlags.Instance);
        if (stringProperty?.GetValue(raw) is string translated)
        {
            value = translated;
            return true;
        }

        value = null;
        return false;
    }

    private static object? GetProperty(object? target, string propertyName) =>
        target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);

    private static IEnumerable<object> GetItems(object target, string propertyName)
    {
        if (GetProperty(target, propertyName) is not IEnumerable enumerable)
        {
            yield break;
        }

        foreach (var item in enumerable)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static int? GetInt(object? target, string propertyName)
    {
        var value = GetProperty(target, propertyName);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static string LogicalTypeName(object value)
    {
        var name = value.GetType().Name;
        foreach (var suffix in new[] { "BinaryOverlay", "BinaryCreateTranslation", "Getter" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
            }
        }

        return name;
    }
}
