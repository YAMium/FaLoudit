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
- SQLite index schema: `5`.
- Diagnostic snapshot schema: `2`.

A schema-1 project configuration is readable only to produce an actionable
migration error; it must be configured again with explicit languages. SQLite
indexes are atomically rebuilt rather than edited in place. Snapshot comparison
requires equal snapshot schemas and, for schema 2, equal language pairs.

## Compatibility rule

Consumers must require top-level `schemaVersion: 2`. Schema-1 consumers must
not interpret schema-2 diagnostic enum values or `source`/`target` roles without
an explicit upgrade.
