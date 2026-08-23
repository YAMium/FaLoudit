# FaLoudit index operations

FaLoudit treats the configured Fallout/MO2 build as read-only. All index, cache, history, and report writes remain inside the selected `.falloutloc` workspace.

## Normal maintenance

Run `faloudit index --status --json` after changing the active MO2 profile, mod list, load order, overwrite contents, or plugin files. Status verifies the current profile/provider fingerprint, validates the expected backend/indexer identity, reports snapshot age and database size, and runs SQLite `quick_check(1)`. An integrity result other than `ok` makes status return exit code 2.

If the result is `stale`, `missing`, or `incompatible`, run normal `faloudit index --json`. It publishes through a staged database and reuses compatible unchanged plugin parses. A failed or cancelled build leaves the previous published database intact.

The last 20 completed builds are recorded in `.falloutloc/logs/index-history.json`. The history contains timing and aggregate counts, not source plugin contents.

## When a full rebuild happens

A normal index run must rebuild the database when:

- the SQLite index schema changed;
- the backend/indexer cache identity changed;
- no compatible published database exists;
- the current database is unreadable or its snapshot is incomplete.

Use `index --rebuild` to force database reconstruction while still permitting reuse of compatible per-plugin data. Use `index --reparse` only when backend extraction correctness is suspect or a complete reparse was explicitly requested; it implies rebuild and disables plugin reuse.

## Corruption and recovery

An `unreadable` freshness state or a SQLite integrity result other than `ok` means the published cache is not trustworthy. Do not edit the database. Run `index --rebuild`; if compatibility itself is suspect, run `index --reparse`. Publication is atomic, so an interrupted attempt can be safely repeated.

Staged files use the `.staged` suffix under `.falloutloc/index` and are cleaned after handled failures. The application never repairs, edits, or rewrites source ESM/ESP files.

## Report snapshots

Named report snapshots live under `.falloutloc/reports/snapshots`. Compare snapshots only when they use the same report kind, filters, confidence threshold, exclusion list, and sufficiently large page limit. If either snapshot is marked truncated, the diff remains usable but incomplete and must not be treated as a complete regression count.
