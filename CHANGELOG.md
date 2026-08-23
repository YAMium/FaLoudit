# Changelog

## 0.4.0 — unreleased

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
