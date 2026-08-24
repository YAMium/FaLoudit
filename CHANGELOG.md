# Changelog

## 0.4.2 — 2026-08-24

- Added MO2-aware indexing of winning loose NVSE scripts from
  `NVSE/Plugins/Scripts`, `NVSE/user_defined_functions`, and
  `NVSE/CompileScript`, including both `.txt` and textual GECK `.gek` sources
  where those formats are used.
- Added searchable values from MO2-winning virtual Data INI files while
  excluding comments, `meta.ini`, `.mohidden` trees, and Windows `desktop.ini`.
- Added synthetic `file:<logical-path>` content identities with physical path,
  source mod, MO2 priority, semantic key/line, encoding evidence, and bounded
  untrusted context for GPT review.
- Preserved CP1250/1251/1252/1254 and unmarked UTF-8 recovery for loose text
  without executing scripts or loading INIs.
- Advanced the SQLite index to schema 7 and included loose-file metadata and
  extractor version in freshness fingerprints.

## 0.4.1 — 2026-08-23

- Added local read-only extraction of hardcoded string GameSettings from the
  installed FO3/FNV GECK executable without bundling Bethesda strings.
- Added searchable synthetic `gmst:<EditorID>` identities and value traces that
  join engine defaults with active ESM/ESP GMST assignments by EditorID.
- Added MO2-aware indexing of Stewie Tweaks `[GameSettings]` assignments from
  the winning main INI and `NVSE/Plugins/Tweaks/Gamesettings` files as an
  after-plugins layer.
- Preserved CP1251 and unmarked UTF-8 INI bytes for strict configured-language
  recovery instead of assuming UTF-8.
- Advanced the SQLite index to schema 6 and included engine/INI inputs in index
  freshness fingerprints.

## 0.4.0 — 2026-08-23

- Added mandatory explicit `sourceLanguage` and `targetLanguage` project settings.
- Added conservative language profiles for Windows-1250, Windows-1251,
  Windows-1252, and Windows-1254 Fallout localizations.
- Generalized indexed language roles and diagnostics from Russian/English to
  target/source while retaining legacy internal enum aliases.
- Added exact-source-reversion detection for target languages that share the
  Latin script with English.
- Advanced project configuration to schema 2, SQLite to schema 5, CLI JSON to
  schema 2, and diagnostic snapshots to schema 2 with explicit migration rules.

## 0.3.2 — 2026-08-23

- Updated the self-contained Windows runtime to .NET 10.0.11 security servicing.
- Updated Microsoft.Data.Sqlite to 10.0.11 and aligned its SQLitePCLRaw 2.1.x dependency graph.
- Updated the xUnit Visual Studio test adapter to 3.1.5.

## 0.3.1 — 2026-08-23

- Prepared the first public release under GPL-3.0-only.
- Added complete third-party and self-contained .NET release notices.
- Made the packaged Codex project independent of a private MO2 profile.
- Added CI, Dependabot, security, and contribution guidance.

## 0.3.0

- Added indexed saved-script content search and semantic analysis fallback.
- Preserved manual read-only investigation when indexed sources do not match.

## 0.2.0

- Added stable search, analysis, diagnostics, coverage, reports, snapshots, and
  incremental index maintenance.

## 0.1.0

- Established the read-only MO2 discovery, plugin backend, SQLite index, search,
  override tracing, and Windows packaging baseline.
