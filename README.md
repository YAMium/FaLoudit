# FaLoudit

[![CI](https://github.com/YAMium/FaLoudit/actions/workflows/ci.yml/badge.svg)](https://github.com/YAMium/FaLoudit/actions/workflows/ci.yml)
[![License: GPL-3.0-only](https://img.shields.io/badge/license-GPL--3.0--only-blue.svg)](LICENSE)

**FaLoudit** is short for **Fallout Localization Auditor**.

Read-only diagnostic CLI for Fallout 3, Fallout: New Vegas, and Tale of Two Wastelands localization conflicts.

The public command and executable are `faloudit` / `faloudit.exe`. The existing `.falloutloc` workspace, `falloutloc.sqlite` database, fingerprint prefix, and internal `FalloutLoc.*` assemblies intentionally retain their v0.1 names so existing configuration, indexes, and caches remain compatible.

The tool is being built to answer:

> Why is this exact text visible, which physical MO2 file and plugin record override won, and where was the Russian translation lost?

## Current production milestone

Implemented:

- strict source/workspace safety boundary;
- portable and split-path MO2 discovery;
- active-profile parsing;
- authoritative `plugins.txt` / `loadorder.txt` consistency check;
- exact reversed `modlist.txt` priority handling;
- separators excluded from mod counts and providers;
- logical Data path to ordered physical provider chain;
- `overwrite` as the highest-priority provider;
- project configuration stored atomically under `.falloutloc/config`;
- `discover`, `configure`, and `doctor` commands;
- JSON output and automated safety/MO2 tests;
- backend-neutral plugin, record, string, and override DTOs;
- exact Mutagen `0.54.4` lock in read-only overlay mode;
- strict CP1252 byte recovery with CP1251/UTF-8/ambiguity evidence;
- semantic extraction for names, dialogue, quests, terminals, messages, notes, perks, factions, body parts, map markers, radio locations, and regions;
- separate content extraction for top-level SCPT source and nested INFO begin/end, quest-stage, terminal-menu, package-event, perk-effect, and patrol result scripts;
- backend-neutral override tracing and xEdit-oracle integration tests;
- versioned SQLite snapshot with physical MO2 provider chains, active plugin metadata, records, strings, language and encoding evidence;
- transactional per-plugin indexing and atomic publication of only a complete database;
- indexed `find` and `trace` commands with JSON output, winning override, physical path, source mod, and MO2 effective priority;
- indexed `content` command with exact/contains/regex modes, bounded context, record winner evidence, untrusted-data flags, and GPT review requirement.
- 0.2 Search API with exact/contains/regex text modes, case handling, plugin/type/category/winner filters, query-bound cursor pagination, exact EditorID lookup, and FormID/FormKey resolution;
- high-level `analyze` command with freshness enforcement, deterministic candidate ranking, ambiguity reporting, full override diagnosis, physical plugin winner, and MO2 source mapping in one call;
- automatic `analyze` fallback from localization fields to content candidates, structured semantic-GPT review, and an explicit manual-search recommendation when both indexes miss;
- stable additive JSON schema v1 with application/command versions, canonical profile context, index state, warnings, confidence, pagination, typed error codes, and documented exit codes;
- index schema v4 with a separate record-content table and record-level `parsed` / `partiallyParsed` / `notApplicable` / `unverified` coverage, plugin-level partial status, a machine-readable extraction catalog, report-query indexes, and `coverage` diagnostics;
- `explain` diagnostics for one FormKey with field-by-field history, RU-to-EN regression status, confidence, encoding evidence, and both winner levels;
- `regressions` and `untranslated` bulk review with plugin/mod/type/category filters, query-bound cursor pagination, confidence thresholds, exact-text exclusions, and diagnostic deduplication;
- conservative `untranslated` review candidates with technical asset-path filtering and an explicit low-confidence caveat;
- structural-change protection for ordinal TERM, MESG, and quest-log lists.
- strict index freshness checks over profile files, load order, physical provider chains, and plugin metadata;
- fresh-index no-op plus explicit `index --status` and `index --rebuild` modes;
- versioned per-plugin cache identity and atomic reuse of unchanged plugin data;
- `index --reparse` for an explicit full backend parse without cache reuse;
- versioned self-contained Windows x64 package with no installed .NET requirement;
- packaging safety check that prevents native bundle extraction outside the workspace;
- atomic Markdown/JSON/CSV/HTML report export under `.falloutloc/reports`;
- named diagnostic snapshots plus `compare` reports for added, resolved, and unchanged localization problems;
- one-pass active-plugin provider mapping, keeping real-profile `analyze` startup near one second;
- `index --status` database size/age/backend/history output and SQLite `quick_check` corruption diagnostics.

Version 0.3 is production-ready for indexed ESM/ESP script-source investigation. See [ROADMAP_0.3.md](ROADMAP_0.3.md) for completed and future content sources. The machine-readable CLI contract is documented in [JSON_CONTRACT_V1.md](JSON_CONTRACT_V1.md), and index maintenance in [OPERATIONS.md](OPERATIONS.md).

The supported localization fields and explicit extraction limitations are documented in [COVERAGE_CATALOG.md](COVERAGE_CATALOG.md).

## Safety

Game, MO2, TTW, profile, mod, `Data`, and `overwrite` directories are read-only sources. The production write API accepts destinations only under:

```text
.falloutloc/config
.falloutloc/cache
.falloutloc/index
.falloutloc/logs
.falloutloc/reports
.falloutloc/samples
.falloutloc/fixtures
```

Lexical path traversal and reparse-point escapes are rejected. Configuration and index publication use staged files in the destination directory followed by an atomic replacement. A failed or cancelled index build preserves the previous database.

## Build and test

This workspace currently uses the pinned .NET SDK in `.falloutloc/cache/dotnet`:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' build '.\FaLoudit.slnx' -c Release
& '.\.falloutloc\cache\dotnet\dotnet.exe' test '.\FaLoudit.slnx' -c Release
```

## Windows package

Create the self-contained Windows x64 package:

```powershell
& '.\scripts\Publish-Windows.ps1'
```

Outputs:

```text
.falloutloc/cache/publish/win-x64/faloudit.exe
.falloutloc/cache/publish/win-x64/e_sqlite3.dll
.falloutloc/cache/packages/faloudit-0.3.2-win-x64.zip
.falloutloc/cache/packages/faloudit-codex-project-0.3.2.zip
.falloutloc/reports/faloudit-win-x64.sha256
```

After extracting the ZIP, run `faloudit.exe` from PowerShell or a terminal. The package includes the .NET runtime; only `faloudit.exe` and its adjacent `e_sqlite3.dll` are required. Keep both files together. Configure a workspace explicitly when the current directory should not own `.falloutloc`:

```powershell
& '.\faloudit.exe' configure 'C:\Modding\My TTW Instance' `
  --profile 'Default' --workspace 'D:\FaLouditWorkspace\.falloutloc'
```

For a ready-to-open Codex project, extract `faloudit-codex-project-0.3.2.zip`. It already contains the root `AGENTS.md`, a first-prompt template, instructions, and the packaged utility under `tools/faloudit`.

## Commands

Read-only discovery; this command writes nothing:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  discover 'C:\Modding\My TTW Instance' --json
```

Save the selected installation and profile into the project workspace:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  configure 'C:\Modding\My TTW Instance' --profile 'Default' --json
```

Validate configuration, profile consistency, safety guard, every active physical plugin winner, Mutagen loading, and known CP1251 recovery:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  doctor --json
```

Build or atomically rebuild the local index from the configured read-only profile:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  index
```

Check freshness without parsing plugins, rebuild while reusing unchanged plugin data, or force a complete backend reparse:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  index --status

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  index --rebuild

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  index --reparse
```

Inspect extraction coverage, record-type statistics, field categories, and bounded samples of unsupported records:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  coverage --issues 100 --json
```

`coverage` exits with code 2 when the snapshot has failed, partially parsed, or unverified content, while still returning a usable report. Older index schemas require one normal rebuild to schema 4; publication remains atomic.

Search indexed localized text, resolve an EditorID or FormID/FormKey, and inspect a complete active override chain:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  find 'Самовыстреливающий дробовик' --ignore-case --winner-only --limit 10

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  edid 'JIPCCCNoNVSE' --json

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  form '0CE224:FalloutNV.esm' --json

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  trace '00CEE9:LonesomeRoad.esm'
```

`find` defaults to case-sensitive substring matching. Select one of `--exact`, `--contains`, or `--regex`; add `--ignore-case`, `--plugin`, `--type`, `--category`, or `--winner-only` as needed. When `nextCursor` is returned, pass it back with `--cursor`; cursors are bound to the original query and filters.


Search saved top-level and nested script source separately from localization fields:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  content 'Bottle Water (Dirty)' --ignore-case --winner-only --limit 10 --json
```

A content hit proves static presence only. Returned context is bounded, marked as untrusted mod data, and requires semantic review before it can be described as a likely runtime source.

Explain where a translation was lost, list regressions, or produce conservative untranslated-review candidates:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  analyze 'New Vegas Medical Clinic' --max-candidates 5 --json

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  explain '0CE224:FalloutNV.esm'

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  regressions 'Better Brotherhood.esm' --limit 100

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  untranslated --limit 100
```

`analyze` is the preferred entry point for a visible problem string. It refuses to diagnose a missing or stale index, ranks exact and partial localization matches, and returns a complete record diagnosis. On `noMatches` it automatically searches indexed script content and emits structured GPT-review evidence. If that also misses, `manualFallbackRecommended` preserves read-only manual investigation of compiled scripts, loose files, archives, and executable strings.

Bulk commands accept `--plugin`, `--mod`, `--type`, `--category`, `--confidence high|medium|low|any`, `--exclude-file`, `--limit`, and `--cursor`. The exclusion file is UTF-8, one exact intentional-English value per line; blank lines and `#` comments are ignored.

Export an atomic Markdown, JSON, CSV, or HTML report into `.falloutloc/reports`:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  report regressions 'Better Brotherhood.esm' --limit 1000 --format markdown

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  report untranslated --limit 1000 --format json
```

Save named diagnostic snapshots and compare them after changing the build:

```powershell
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  report regressions --limit 10000 --format html --snapshot before-update

# Update/reindex the external build, then capture the same filters and limit.
& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  report regressions --limit 10000 --format html --snapshot after-update

& '.\.falloutloc\cache\dotnet\dotnet.exe' run --project '.\src\FalloutLoc.Cli' -c Release -- `
  compare before-update after-update --format html
```

Snapshots are written only under `.falloutloc/reports/snapshots`. A truncated snapshot is explicitly marked and produces an incomplete-comparison warning. `compare` exits with code 2 when new problems are present, while still returning a usable report.

Use `--workspace <path>` to override the default `<current-directory>/.falloutloc` workspace. It must not overlap any source root.

## Reference production validation

FaLoudit 0.3 was validated against a private TTW profile with:

- 252 active plugins;
- all 252 physical plugin winners resolved;
- `plugins.txt` and `loadorder.txt` match exactly.
- index schema 4 / indexer 6 / field catalog 1: 0 failed plugins, 11,895 Script coverage gaps across 190 partially parsed plugins;
- 2,516,997 records, 2,529,586 localized string fields, and 52,640 saved top-level/nested script sources in the production index;
- measured script-fallback `analyze`: 1.14–1.22 s after warm-up; full schema-4 rebuild: 46.3 s; SQLite `quick_check`: `ok`.

Local production reports are intentionally excluded from source control because
they describe the user's private MO2 profile. The reproducible behavior and
limitations are documented in `COVERAGE_CATALOG.md`, `JSON_CONTRACT_V1.md`, and
the automated tests.

## License

Copyright (C) 2026 YAMium.

FaLoudit is free software licensed under GNU GPL version 3 only. See `LICENSE`,
`THIRD-PARTY-NOTICES.md`, and `SOURCE.md`.

FaLoudit is an unofficial community project and is not affiliated with or
endorsed by Bethesda Softworks, ZeniMax Media, Obsidian Entertainment, or the
Mod Organizer 2 team. Fallout and related trademarks belong to their respective
owners.
