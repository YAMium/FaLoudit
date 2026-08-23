# FaLoudit 0.4 multilingual design

## Goal

Replace the implicit English-to-Russian assumption with an explicit localization pair:

```json
{
  "sourceLanguage": "en",
  "targetLanguage": "ru"
}
```

Both values are normalized BCP-47-style language tags. FaLoudit 0.4 supports the Fallout
European single-byte localization families covered by Windows-1250, Windows-1251,
Windows-1252 and Windows-1254. Unsupported tags fail configuration instead of silently
using the wrong decoder.

## Compatibility and migration

- The public executable remains `faloudit`.
- `.falloutloc`, `falloutloc.sqlite`, the fingerprint prefix and `FalloutLoc.*` assemblies
  remain unchanged.
- Project configuration advances from schema 1 to schema 2. A schema-1 file can still be
  read, but language-sensitive commands require the user to run `configure` again with
  explicit `--source-language` and `--target-language` values.
- SQLite advances from schema 4 to schema 5. Language settings become snapshot metadata,
  and indexed string roles are `Source`, `Target`, `Other` or `Empty`. An old index is never
  modified in place; normal index publication atomically replaces it after a rebuild.
- Diagnostic snapshots advance to schema 2 and use `earlierTargetText`. Comparisons remain
  supported for two schema-1 snapshots and for two schema-2 snapshots, but not across
  semantically different schemas.

## Detection rules

The configuration determines roles, decoding candidates and report wording. It does not
pretend that every short string can be identified reliably.

1. Empty values are `Empty`.
2. A target-specific script or distinguishing letters are `Target`.
3. A source-specific script or distinguishing letters are `Source`.
4. Plain Latin/ASCII text is treated as `Source` when the configured source language uses
   Latin. This is only a low-confidence untranslated-review signal without override history.
5. A target value followed by an exact return to an earlier source value is a
   high-confidence translation regression.
6. Shared-script values without distinguishing evidence remain conservative; ambiguity is
   reported rather than guessed.

This keeps English-to-Russian behavior while allowing English-to-Polish, German, French,
Spanish, Italian, Portuguese, Czech, Slovak, Hungarian, Turkish, Ukrainian, Belarusian and
Bulgarian projects to use the same pipeline.

## Safety

Game, MO2, profile and mod directories remain read-only sources. Test downloads and any
mutated fixture copies belong only under `.falloutloc/fixtures` or `.falloutloc/cache`.
