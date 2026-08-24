# FaLoudit Localization Coverage Catalog

Catalog version: 1
Index schema: 7

This catalog describes user-visible FO3/FNV/TTW plugin fields that the current read-only Mutagen backend extracts and indexes. `faloudit coverage --json` embeds the same machine-readable `supportedFields` catalog and reports actual counts from the published snapshot.

## Coverage statuses

Record statuses are:

- `parsed` — all localization fields covered by the current contract were inspected;
- `partiallyParsed` — an expected field or nested structure could not be read; warnings identify the failure;
- `notApplicable` — the record type was audited and has no known localized user-facing field in the current scope;
- `unverified` — the record type may contain visible text but no complete extraction contract exists.

A plugin is `partiallyParsed` when it contains at least one `partiallyParsed` or `unverified` record. This is distinct from `failed`: usable records and strings from a partially parsed plugin remain indexed and searchable.

The compatibility-sensitive `parsedPlugins` count in general index status includes both fully and partially parsed usable plugins. The `coverage` report provides mutually exclusive `parsedPlugins`, `partiallyParsedPlugins`, and `failedPlugins` counts.

## Supported fields

Common root fields are detected by actual record interfaces/properties:

| Semantic path | Category |
|---|---|
| `Name` | `display-name` |
| `Description` | `description` |
| `ShortName` | `short-name` |
| `Abbreviation` | `abbreviation` |
| `ActivationPrompt` | `activation-prompt` |
| `VatsAttackName` | `vats-attack-name` |
| `DumbResponse` | `dialogue-dumb-response` |
| `Prompt` | `dialogue-prompt` |

Specialized nested contracts are:

| Record type | Semantic path pattern | Category |
|---|---|---|
| `GameSettingString` | `Data` | `game-setting` |
| `DialogResponses` | `Responses[number=*,occurrence=*].ResponseText` | `dialogue-response` |
| `Quest` | `Stages[index=*].LogEntries[*].Entry` | `quest-log` |
| `Quest` | `Objectives[index=*,occurrence=*].Description` | `quest-objective` |
| `Terminal` | `MenuItems[*].ItemText` | `terminal-menu` |
| `Terminal` | `MenuItems[*].ResultText` | `terminal-result` |
| `Message` | `MenuButtons[*].Text` | `message-button` |
| `Note` | `Data.Text` | `note-text` |
| `Perk` | `Effects[type=*,rank=*,priority=*,entryPoint=*,occurrence=*].ButtonLabel` | `perk-activation-button` |
| `Faction` | `Ranks[number=*].Name.Male/Female` | `faction-rank` |
| `BodyPartData` | `Parts[actorValue=*,type=*,occurrence=*].Name` | `body-part-name` |
| `PlacedObject` | `MapMarker.Name` | `map-marker` |
| `PlacedObject` | `AudioData.LocationName` | `radio-location` |
| `Region` | `MapName.Map` | `region-map-name` |

## Engine GameSettings

FaLoudit 0.4.1 also indexes string GameSettings initialized outside ordinary
plugin records. The default EditorID/value catalog is extracted read-only from
the user's installed `GECK.exe`; Bethesda strings are not bundled with
FaLoudit. FNV and TTW use the New Vegas GECK/runtime pair, while Fallout 3 uses
its own GECK/runtime pair.

GameSettings have no meaningful plugin FormID at the engine-default layer, so
FaLoudit exposes `gmst:<EditorID>` identities such as `gmst:sHowMany`. A trace
joins values by EditorID in this order:

1. engine default;
2. active ESM/ESP `GameSettingString.Data` assignments;
3. MO2-winning Stewie Tweaks `[GameSettings]` INIs applied after plugins.

If GECK is unavailable or its constructor table cannot be validated, plugin
indexing continues and the snapshot exposes an actionable catalog warning.

## Current explicit limitation

`find` covers only audited localization fields. FaLoudit 0.3 indexes saved source from top-level `Script` records and nested INFO begin/end, quest-stage, terminal-menu, package-event, perk-effect, and patrol scripts in the separate `content` layer. `analyze` queries that layer automatically only after localization fields have no match.

FaLoudit 0.4.2 extends that content layer with MO2-winning loose NVSE scripts
under `NVSE/Plugins/Scripts` and `NVSE/user_defined_functions`, plus key values
from MO2-winning virtual Data INI files. Script results store quoted literals
with their executable source line; INI results store section/key, value, and
line number. Comments, MO2 `meta.ini`, `.mohidden` trees, and Windows
`desktop.ini` are excluded.

Saved or loose source proves static presence, not runtime execution. Compiled
bytecode without source and BSA content are not decoded. A miss therefore emits
`manualFallbackRecommended` instead of claiming absence. Script coverage gaps
remain visible and are not plugin corruption.

Engine string coverage is limited to validated `s*` GameSettings. Arbitrary
hardcoded executable text that is not part of that catalog remains outside the
index and may require manual read-only investigation.

## Correctness evidence

Automated fixtures cover CP1251 recovery, stable FormKeys, override winners, dialogue responses, terminal menu/result fields, body-part names, partial/unverified persistence, and incremental cache reuse. Selected real chains are compared with copied xEdit-oracle plugins under the project workspace.

Unsupported or newly encountered record types are classified as `unverified` instead of being silently treated as text-free.
