# FaLoudit JSON Contract — Schema Version 2

Schema version 2 is the FaLoudit 0.4 machine-readable contract. The common
envelope, exit codes, typed errors, pagination, and command payload locations
remain as documented in [JSON_CONTRACT_V1.md](JSON_CONTRACT_V1.md), except for
the deliberate multilingual changes below.

## Language pair

`configure` requires both:

```text
--source-language <tag> --target-language <tag>
```

The serialized project configuration contains normalized `sourceLanguage` and
`targetLanguage` fields. `indexState` and index snapshot metadata expose the
same pair. An index is not reusable when its pair differs from configuration.

## Generic language roles

Indexed string `language` values are now:

- `source`;
- `target`;
- `other`;
- `empty`.

They describe a role relative to the configured pair, not an assertion that a
string is universally English or Russian.

Diagnostic status names changed accordingly:

- `localizedTarget`;
- `translationRegression`;
- `clearedTranslation`;
- `nonTargetRegression`;
- `sourceWithoutActiveTarget`;
- `emptyWinner`;
- `deletedWinner`;
- `neutral`;
- `ambiguous`.

Field diagnostics use `earlierTarget`; rendered/snapshot evidence uses
`earlierTargetText`.

## Versioned stores

- Project configuration schema: `2`.
- SQLite index schema: `7`.
- Diagnostic snapshot schema: `2`.

A schema-1 project configuration is readable only to produce an actionable
migration error; it must be configured again with explicit languages. SQLite
indexes are atomically rebuilt rather than edited in place. Snapshot comparison
requires equal snapshot schemas and, for schema 2, equal language pairs.

## Engine GameSettings

FaLoudit 0.4.1 adds string GameSettings that are initialized by the game
runtime rather than stored as ordinary plugin records. They use a stable
synthetic identity:

```text
gmst:<EditorID>
```

For example, `gmst:sHowMany` is not a FormKey and must not be interpreted as a
hexadecimal FormID. `find` and `analyze` can return these identities, `edid`
resolves an exact setting name, and `trace gmst:<EditorID>` returns the ordered
value chain:

1. engine default extracted locally from GECK;
2. active ESM/ESP `GameSettingString` assignments matched by EditorID;
3. winning Stewie Tweaks `[GameSettings]` INI assignments applied after ESPs.

String search results add `sourceKind` with `engineDefault`, `plugin`, or
`postPluginIni`. Index snapshot metadata adds `engineGameSettings`,
`postPluginGameSettingOverrides`, `engineGameSettingCatalogStatus`, optional
catalog/runtime paths, and warnings. These are additive schema-2 fields.

## Loose content

FaLoudit 0.4.2 adds MO2-winning loose NVSE script literals from supported `.txt`
and textual GECK `.gek` sources, plus virtual Data INI values, to `content` and
`analyze.contentFallback`. They use a synthetic identity:

```text
file:<logical Data path>
```

This is a file identity, not a FormKey or FormID. Loose results add
`sourceKind: looseScript | iniValue`, `lineNumber`, logical path in
`pluginName`, physical winner, source MO2 mod, semantic line or INI key, and
bounded `context`. `loadOrderIndex` is `-1` because files have no plugin load
order. `isWinningOverride` is true because only the MO2 physical
winner for a logical path is indexed. It does not assert that a script executed
or that an INI consumer used the value.

Index snapshot metadata adds `looseContentFiles`, `looseContentEntries`, and
warnings. These fields are additive within top-level JSON schema 2.

FaLoudit 0.4.3 adds `sourceKind: uiXmlText` and `recordType: UiXml` for literal
text from MO2-winning `Menus/**/*.xml`. The existing synthetic file identity,
line number, semantic path, context, and trust rules apply. Loose content
matches also add `physicalProviders`, ordered from highest to lowest effective
MO2 priority; exactly one provider is marked `isWinner` when resolution
succeeds. These additions do not change top-level JSON schema 2 or SQLite
schema 7.

## Compatibility rule

Consumers must require top-level `schemaVersion: 2`. Schema-1 consumers must
not interpret schema-2 diagnostic enum values or `source`/`target` roles without
an explicit upgrade.
