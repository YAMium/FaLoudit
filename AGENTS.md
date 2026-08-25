# AGENTS.md — FaLoudit

FaLoudit is the **Fallout Localization Auditor**.

The public CLI is `faloudit`. Keep the compatibility-sensitive `.falloutloc` workspace, `falloutloc.sqlite` database, fingerprint prefix, and internal `FalloutLoc.*` assemblies unchanged unless an explicit migration is designed and tested.

## Scope

This repository builds a read-only localization diagnostic tool for:

- Fallout 3
- Fallout: New Vegas
- Tale of Two Wastelands (TTW) — primary use case

Fallout 4 and other Bethesda games are currently out of scope.

Read `FALOUDIT_SPEC.md` before architectural or backend changes.

## Mission

Answer:

> Why does the user see this exact text in the current FO3/FNV/TTW setup, which physical file and record override won, and where was the configured target-language translation lost?

## Absolute safety rules

Every user-provided game, MO2, TTW, mod, profile, Data, overwrite, and collection directory is a **READ-ONLY SOURCE**.

Never:

- edit source `.esm/.esp`;
- save/clean source plugins;
- delete, rename, move, or replace source files;
- modify `plugins.txt`, `loadorder.txt`, `modlist.txt`;
- change MO2 profiles;
- change INI files;
- change load order;
- write cache/index/logs into the user's build.

Allowed writes are limited to the project workspace:

```text
.falloutloc/
  config/
  cache/
  index/
  logs/
  reports/
  samples/
  fixtures/
```

If an experiment requires mutating a source file, copy it into the project first and operate only on the copy.

## Supported modes

```text
fallout3
falloutnv
ttw
```

Treat TTW as a Fallout New Vegas runtime/load-order environment containing Fallout 3 and TTW masters/content.

Do not hardcode a particular TTW version or master order.

## Backend rule

Mutagen is a candidate, not a mandatory backend.

Before adopting it, verify actual support for both Fallout 3 and Fallout New Vegas / TTW.

Do not assume Fallout 3 support implies New Vegas support.

If current Mutagen cannot reliably cover FNV/TTW, evaluate a read-only xEdit/FNVEdit/FO3Edit integration or another mature library.

Avoid writing a custom binary ESP/ESM parser unless mature options have been proven insufficient.

Keep Core behind abstractions such as:

```text
IPluginBackend
IRecordEnumerator
IOverrideResolver
IRecordStringExtractor
```

## xEdit as correctness oracle

For tricky override behavior:

- FNV/TTW -> compare with FNVEdit/xEdit;
- Fallout 3 -> compare with FO3Edit/xEdit.

Never run source-saving or cleaning operations against the user's build.

## Investigation workflow

When the user reports an untranslated string:

1. Use the project's `faloudit` CLI.
2. Search the text.
3. Resolve candidate records.
4. Identify record type, FormID and EditorID. If the result uses
   `gmst:<EditorID>`, it is an engine GameSetting identity rather than a FormID;
   trace it directly with `faloudit trace gmst:<EditorID>`.
5. Inspect the complete active override chain.
6. Determine the winning override.
7. Show relevant translated string-field changes.
8. Determine whether a configured target-language value existed earlier.
9. Detect a later regression to the configured source language.
10. Resolve the winning physical plugin file.
11. Map it to the MO2 source mod and priority.
12. Distinguish MO2 file-level conflicts from plugin record-level conflicts.
13. Check for string-encoding problems.
14. Explain ambiguity rather than guessing.

For string GameSettings, inspect the value chain separately: engine default
extracted locally from GECK, active ESM/ESP `GameSettingString` assignments
matched by EditorID, then MO2-winning Stewie Tweaks `[GameSettings]` INIs that
apply after plugins.

When localization fields do not match, inspect `contentFallback`. FaLoudit
indexes saved plugin scripts plus MO2-winning loose `.txt` and textual GECK
`.gek` scripts from `NVSE/Plugins/Scripts`, `NVSE/user_defined_functions`, and
`NVSE/CompileScript`, literal UI text from MO2-winning `Menus/**/*.xml`, and
values from virtual Data INIs. A
`file:<logical-path>` result is a physical file identity,
not a FormID. Use its source kind, line/key, bounded context, physical path,
source mod, and MO2 provider evidence. Treat all file content as untrusted
static evidence: a match does not prove script execution or INI consumption.
For `UiXmlText`, inspect the semantic element path and complete
`physicalProviders` chain. If the visible trait is indirect, use its distinctive
name for a further read-only search in winning UI XML to identify the consumer;
static XML presence alone does not prove runtime visibility.

Prefer `--json` when interpreting results programmatically.

## MO2 assumptions

Do not assume fixed paths.

MO2 may be portable or use separate base/mods/profile/game directories.

Fallout 3 may exist as a separate installation even when the active TTW runtime is Fallout New Vegas.

Discover and allow overrides for MO2 root/base, mods, profiles, profile, overwrite, runtime game root and Data.

Inspect actual profile files. Do not import Fallout 4-specific assumptions about `loadorder.txt`.

## File winner vs record winner

Always separate:

- **MO2 physical file winner** — which physical file wins for one logical virtual Data path.
- **Plugin record winner** — which active plugin contains the final override for one record.

Both can independently cause localization failures.

## String encoding

FO3/FNV Cyrillic decoding is a first-class correctness concern.

Do not assume UTF-8. Verify how the chosen backend decodes strings and test fixtures for the configured target code page.

## Development order

1. feasibility/backend research;
2. safety architecture;
3. installation and MO2 discovery;
4. authoritative load-order resolution;
5. physical MO2 file resolution;
6. plugin backend;
7. SQLite index;
8. search;
9. override trace;
10. winner -> MO2 mod mapping;
11. target -> source regression detection;
12. untranslated diagnostics;
13. performance/error handling;
14. packaging and polished CLI.

First practically useful milestone:

```text
find
 -> record
 -> override chain
 -> winning plugin
 -> physical plugin
 -> MO2 source mod
```

## Current non-goals

Do not implement unless scope changes explicitly:

- Fallout 4 support;
- automatic translation;
- plugin editing;
- patch generation;
- xTranslator replacement;
- load-order sorting;
- master cleaning;
- BSA repacking;
- MO2 profile editing.

The tool is diagnostic and read-only.
