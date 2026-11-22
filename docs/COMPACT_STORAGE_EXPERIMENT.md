# Compact SQLite string fields: schema 8 benchmark

The control is the schema-7 exact-search/partial-index change at `dc3b784`.
Both variants keep SQLite, the plugin parser, the CLI contract, and the freshness
checks. The compact format tested here shipped in 0.4.6. For the normal
one-time rebuild, see [index operations](../OPERATIONS.md). To reproduce
the experiment itself, use separate workspaces for the two executable versions.

Only fields with SQL NULL text, language `Empty`, encoding evidence `None`,
no byte hash, and no ambiguity are packed. A row keeps its original occurrence
ID, record ID, and a reference to the exact (field path, category) pair. Empty
strings, ambiguous values and all other combinations retain their full representation.
The logical `strings` view restores the previous columns and ordering by ID;
an absent field remains distinguishable from a field present with a NULL value.
Unused shape definitions can remain until a clean rebuild; these are metadata,
not searchable occurrences. Readers must not filter empty overrides before
deciding which GameSetting or translation won.

## Paired benchmark

Release CLI, Windows x64, one real TTW en→ru profile, 253 active plugins.
Two isolated workspaces read the same MO2 inputs. A local MO2 fixture points to
the real mods/profile/Data read-only; its own overwrite contains copies of the
relevant original overwrite files and one winning ESP. Changing one localized
quest field in this **copy** preserves the field byte length. Both variants see
identical paths, bytes and timestamps in each phase.

Clean runs start with an empty index and reparse all plugins. Incremental runs
start from that variant's own clean snapshot after the one-file edit. Times
are wall clock for the full `index --json` command, including discovery and
freshness verification. OS disk caches were not flushed. Follow-up runs
alternate which variant goes first. File sizes are the published databases,
not peak disk usage during staged replacement.

| Full command / database | Control | Compact |
|---|---:|---:|
| Clean, seconds (three runs; median) | 40.366 / 38.507 / 38.062; **38.507** | 39.923 / 37.120 / 35.902; **37.120** |
| One-plugin update, seconds (three runs; median) | 11.235 / 10.806 / 10.893; **10.893** | 10.807 / 11.285 / 10.899; **10.899** |
| Clean database, bytes | 942,088,192 | 734,380,032 |
| Updated database, bytes | 942,837,760 | 735,109,120 |

All clean runs parsed 253 plugins; all updates parsed one, reused 252, and had
zero parser failures. The median time **inside the builder** was 30.437 vs
28.059 s clean, and 2.913 vs 3.039 s incremental. The reliable benefit here
is 198.1 MiB less storage (22.0% of the whole clean database); the end-to-end
clean-build difference was modest and the incremental difference negligible.
The first pair included other development checks; the other two pairs ran
serially in opposite order. This is one profile and machine, not a general
speed guarantee or a test with a cold OS cache.

## Equivalence checks

For each compared pair of finished databases:

```text
python scripts/compare-compact-snapshots.py <schema-7.sqlite> <schema-8.sqlite>
```

The verifier opens both databases read-only, checks the expected schema marker,
SQLite integrity and foreign keys, and streams every logical row in ID order,
including the reconstructed string fields. Only snapshot creation time and the
expected storage version in the backend identity are excluded. It does not
ignore paths, fingerprints, byte hashes, empty values, plugin status or IDs.

The pilot pair was verified both before and after the one-plugin change:
2,519,559 records, 2,532,588 string occurrences, 53,217 script-content rows,
4,830 physical providers and 109,823 loose-content entries matched.
The compact database contains 2,052,808 packed occurrences using 229 distinct
field shapes.

Twelve paired CLI responses were compared, excluding only per-workspace paths,
snapshot creation times and storage-version metadata. Five trace pairs included
a quest before/after the edit, a GameSetting, a script, and a record with a NULL
activation prompt. Seven analyze pairs covered the original and changed quest
text, misses, and positive UI XML and embedded-script fallback matches. The
old text stopped resolving and the new text resolved to the winning copy with
high confidence. A filtered regressions report and an untranslated report were
also compared. No game data, private profile paths, binaries or snapshots are
part of this branch.
