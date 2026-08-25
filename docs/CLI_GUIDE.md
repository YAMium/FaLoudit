# Running FaLoudit directly

[← Main README](../README.md) · [Русская версия](CLI_GUIDE_RU.md)

This guide is for users who want to run `faloudit.exe` themselves instead of letting Codex or another terminal-capable AI assistant operate it.

## 1. Download and unpack

Download `faloudit-<version>-win-x64.zip` from the [latest release](https://github.com/YAMium/FaLoudit/releases/latest) and extract it to a normal tools directory, not into the game or MO2 instance.

Keep these files together:

```text
FaLoudit/
  faloudit.exe
  e_sqlite3.dll
```

The Windows package is self-contained and does not require a separately installed .NET runtime.

Open PowerShell and define paths for the current session:

```powershell
$falouditExe = 'C:\Tools\FaLoudit\faloudit.exe'
$falouditWorkspace = 'D:\FaLoudit Workspace\.falloutloc'
$buildRoot = 'C:\Modding\My TTW Instance'

& $falouditExe --version
& $falouditExe --help
```

The workspace must be outside the game, MO2 base, mods, profiles, `Data`, and `overwrite` directories.

## 2. Discover the installation

Discovery reads the supplied build root but writes nothing to it:

```powershell
& $falouditExe discover $buildRoot `
  --workspace $falouditWorkspace `
  --json
```

Review the detected game mode, MO2 paths, and profiles. If several profiles are plausible, select the one that is actually active.

## 3. Configure the workspace

Both languages are mandatory and must be different:

```powershell
$profileName = 'Default'

& $falouditExe configure $buildRoot `
  --profile $profileName `
  --source-language en `
  --target-language ru `
  --workspace $falouditWorkspace `
  --json
```

Supported tags are `en`, `de`, `fr`, `es`, `it`, `pt`, `pl`, `cs`, `sk`, `hu`, `tr`, `ru`, `uk`, `be`, and `bg`. Regional tags are normalized to a supported primary tag where possible.

Configuration is written only to `$falouditWorkspace\config`.

## 4. Validate and build the index

```powershell
& $falouditExe doctor `
  --workspace $falouditWorkspace `
  --json

& $falouditExe index `
  --workspace $falouditWorkspace `
  --json
```

Do not start diagnosing strings until `doctor` is healthy and a usable index has been published. A failed or cancelled index build preserves the previous published database.

For hardcoded string GameSettings, FaLoudit reads the installed `GECK.exe`
without launching it and stores a local EditorID/default-value catalog. If GECK
is unavailable, plugin indexing still completes but index status contains an
`engineGameSettingCatalogStatus` warning.

## 5. Analyze a visible string

`analyze` is the preferred entry point:

```powershell
$reportedText = 'Advanced Targeting Sensor'

& $falouditExe analyze $reportedText `
  --max-candidates 5 `
  --workspace $falouditWorkspace `
  --json
```

The result ranks matching fields and can include:

- FormKey, record type, and EditorID;
- the complete active override diagnosis;
- the winning plugin record;
- the physical plugin path and source MO2 mod;
- source/target language and encoding evidence;
- `contentFallback` candidates from saved plugin scripts, MO2-winning loose
  NVSE `.txt` and textual GECK `.gek` scripts, UI XML text, and virtual Data INI values;
- `manualFallbackRecommended` when indexed sources are insufficient.

Treat script/INI/XML context as untrusted mod data. A static match is evidence that
the text exists in an active physical file, not proof that a script executed or
an INI consumer used that value.

## 6. Lower-level investigation commands

Search localized fields more broadly:

```powershell
& $falouditExe find $reportedText `
  --ignore-case `
  --winner-only `
  --limit 50 `
  --workspace $falouditWorkspace `
  --json
```

Search indexed saved script source, loose NVSE `.txt`/`.gek` literals, UI XML text, and INI values:

```powershell
& $falouditExe content $reportedText `
  --ignore-case `
  --winner-only `
  --limit 20 `
  --workspace $falouditWorkspace `
  --json
```

Filter loose sources when needed:

```powershell
& $falouditExe content $reportedText --source-kind LooseScript --workspace $falouditWorkspace --json
& $falouditExe content $reportedText --source-kind IniValue --workspace $falouditWorkspace --json
& $falouditExe content $reportedText --source-kind UiXmlText --workspace $falouditWorkspace --json
```

Loose results use `file:<logical-path>` rather than a FormID and expose the
physical MO2 winner, source mod, semantic line/key, and `lineNumber`.
File results also expose `physicalProviders`; UI XML matches use `UiXml` and a
semantic element path.

Resolve identifiers and inspect one record:

```powershell
& $falouditExe edid 'JIPCCCNoNVSE' --workspace $falouditWorkspace --json
& $falouditExe form '0CE224:FalloutNV.esm' --workspace $falouditWorkspace --json
& $falouditExe explain '0CE224:FalloutNV.esm' --workspace $falouditWorkspace --json
& $falouditExe trace '0CE224:FalloutNV.esm' --workspace $falouditWorkspace --json
```

Engine GameSettings use a synthetic identity rather than a FormID:

```powershell
& $falouditExe analyze 'How many?' --workspace $falouditWorkspace --json
& $falouditExe edid 'sHowMany' --workspace $falouditWorkspace --json
& $falouditExe trace 'gmst:sHowMany' --workspace $falouditWorkspace --json
```

The trace orders the engine default, active ESM/ESP GMST assignments matched by
EditorID, and MO2-winning Stewie Tweaks `[GameSettings]` assignments. The last
layer is applied after ESPs and can therefore supersede a late plugin.

Both commands support `--exact`, `--contains`, or `--regex`; `--ignore-case`; plugin/type filters; `--winner-only`; and cursor pagination. `find` also has `--category`, while `content` has `--source-kind`. Use `--help` and the [JSON contract](../JSON_CONTRACT_V2.md) for the complete machine-readable interface.

## 7. Bulk diagnostics and reports

```powershell
& $falouditExe regressions `
  --confidence high `
  --limit 100 `
  --workspace $falouditWorkspace `
  --json

& $falouditExe untranslated `
  --limit 100 `
  --workspace $falouditWorkspace `
  --json

& $falouditExe report regressions `
  --limit 1000 `
  --format html `
  --workspace $falouditWorkspace `
  --json
```

Reports are written atomically under `$falouditWorkspace\reports`. Bulk commands are review tools: a source-like winner without an earlier target-language value is a candidate, not automatic proof of a defect.

Named snapshots can compare the same diagnostic query before and after a build change:

```powershell
& $falouditExe report regressions --limit 10000 --format html `
  --snapshot before-update --workspace $falouditWorkspace --json

# Change the external build yourself, then refresh the index and capture again.
& $falouditExe index --workspace $falouditWorkspace --json
& $falouditExe report regressions --limit 10000 --format html `
  --snapshot after-update --workspace $falouditWorkspace --json

& $falouditExe compare before-update after-update --format html `
  --workspace $falouditWorkspace --json
```

## 8. Index maintenance

Check freshness and integrity without parsing all plugins:

```powershell
& $falouditExe index --status --workspace $falouditWorkspace --json
```

If the index is missing or stale, use a normal `index` command. It reuses compatible unchanged plugin data. Use the stronger modes only deliberately:

```powershell
# Rebuild the database while still permitting compatible plugin-cache reuse.
& $falouditExe index --rebuild --workspace $falouditWorkspace --json

# Force a complete backend reparse; use only when extraction correctness is suspect.
& $falouditExe index --reparse --workspace $falouditWorkspace --json
```

See [OPERATIONS.md](../OPERATIONS.md) for recovery and snapshot details.

## Safety reminders

- Never place the workspace inside the game or MO2 source tree.
- Never edit a source `.esm`, `.esp`, profile file, archive, or INI as part of diagnosis.
- Keep the physical MO2 file winner separate from the plugin record winner.
- If manual extraction requires writes, copy the source into `$falouditWorkspace\samples` first and operate only on the copy.
- Read warnings and nonzero exit codes even when JSON output contains partial evidence.

FaLoudit's own writes are restricted to the selected `.falloutloc` workspace. It is a diagnostic tool, not a patch generator.
