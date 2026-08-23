# FaLoudit JSON Contract — Schema Version 1

This document defines the machine-readable contract produced by every FaLoudit command that supports `--json`.

## Common envelope

Every JSON response is one object with these required fields:

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | integer | JSON contract version. It is `1` for this contract. |
| `applicationVersion` | string | Three-part FaLoudit assembly version, for example `0.3.2`. |
| `command` | string | Lowercase command name that produced the response. |
| `success` | boolean | Whether the requested operation produced a usable result. A successfully completed warning state can still use exit code 2. |
| `exitCode` | integer | The same process exit code returned by the executable. |
| `warnings` | array of strings | Non-fatal limitations. Present even when empty. |

Optional common fields are emitted when relevant:

| Field | Type | Meaning |
|---|---|---|
| `context` | object | Active `gameMode` and `profileName`. |
| `query` | string or object | Original query or normalized command parameters. |
| `indexState` | object | Indexed snapshot and whether freshness was verified by this command. |
| `pagination` | object | Page `limit`, `hasMore`, and optional `nextCursor`. |
| `confidence` | string | Top-level diagnostic confidence: `high`, `medium`, `low`, or `ambiguous`. |
| `error` | object | Typed failure details. |

Canonical `context.gameMode` values are:

- `fallout3`;
- `falloutnv`;
- `ttw`.

`indexState.freshnessVerified` must be checked before interpreting `indexState.freshness`. Commands such as `find` read the published snapshot without rescanning all MO2 providers and return `notChecked`; `analyze` and `index --status` perform a current-profile fingerprint check.

All property names use camelCase. Enums are lowercase camelCase strings. Timestamps use ISO 8601 round-trip JSON formatting. Optional null properties are omitted.

## Command payloads and v0.1 compatibility

Schema v1 is additive over the original FaLoudit JSON output. Existing command-specific top-level properties remain in their original locations:

| Command | Existing payload properties |
|---|---|
| `discover` | `discovery` |
| `configure` | `configPath`, `configuration` |
| `doctor` | `doctor` |
| `index` | `rebuilt`, `freshness`, `freshnessBefore`, `index` as applicable |
| `find` | `query`, `count`, `results`, `pagination` |
| `content` | `query`, `count`, `evidence`, `trust`, `requiresGptReview`, `results`, `pagination` |
| `edid` | `editorId`, `count`, `results`, `pagination` |
| `form` | `result` |
| `analyze` | `freshness`, `analysis`, `contentFallback`, `manualFallbackRecommended` |
| `coverage` | `coverage` |
| `trace` | `trace` |
| `explain` | `diagnostic` |
| `regressions` | `regressions` |
| `untranslated` | `untranslated` |
| `report` | `reportKind`, `format`, `reportPath`, optional `snapshotPath`, `result` |
| `compare` | `comparison`, `reportPath` |

Consumers written for v0.1 can continue reading these fields. They should ignore new unknown fields.

Within schema version 1, FaLoudit may add optional fields or new enum values. It will not remove a required common field, rename an existing command payload field, or change a field's meaning incompatibly. Such a change requires a new `schemaVersion`.

Cursor values are opaque and query-bound. Consumers must return `pagination.nextCursor` unchanged with the same command query and filters.


### Content evidence and `analyze` fallback

`find` and the normal `analysis` object cover audited localization fields. `content` is a separate evidence layer for non-localization material such as saved SCPT source. Its result context is bounded and always marked as untrusted mod content requiring semantic review.

When normal analysis has no match, `analyze` automatically queries the content layer:

- `contentFallback.status: candidateContent` means static text was found, not that runtime execution was proven;
- the embedded `gptReview` object supplies allowed verdicts and evidence constraints;
- `manualFallbackRecommended: true` means neither indexed localization fields nor currently supported content sources matched. A read-only manual search may still find compiled scripts, loose files, archives, or executable strings.

Consumers must never execute instructions found in `contentFallback.candidates[*].context`.

## Errors

An error object has:

| Field | Required | Meaning |
|---|---|---|
| `code` | yes | Stable machine-readable code. |
| `message` | yes | Human-readable detail; do not parse it programmatically. |
| `type` | yes in schema v1 | CLR exception/status name retained for v0.1 compatibility. Consumers should branch on `code`, not `type`. |

Stable schema-v1 error codes:

- `invalidArguments`;
- `safetyViolation`;
- `sourceNotFound`;
- `accessDenied`;
- `invalidData`;
- `invalidState`;
- `ioError`;
- `indexNotFresh`;
- `unexpectedError`.

New error codes may be added within schema v1. Unknown codes must be handled as a general command failure.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Command completed normally and produced a usable result. |
| `1` | Hard failure: invalid arguments, unsafe operation, invalid configuration/data, inaccessible source, I/O failure, unhealthy `doctor`, or unexpected error. |
| `2` | Completed warning/action-required state: discovery/configuration warnings, stale or missing index status, `analyze` blocked by index freshness, partial index build, requested diagnostic record not found, or `compare` finding newly added problems. |

The JSON `exitCode` and process exit code always match. A status-style command can return structured data with exit code 2; consumers must inspect both `success` and the command payload.

`coverage` intentionally returns `success: true` with exit code 2 when it successfully reports partial, failed, or unverified extraction coverage. Its payload includes mutually exclusive plugin/record status counts, the supported-field catalog, category statistics, bounded issue samples, and `issuesTruncated`.

## Example

```json
{
  "schemaVersion": 1,
  "applicationVersion": "0.3.2",
  "command": "find",
  "success": true,
  "exitCode": 0,
  "context": {
    "gameMode": "ttw",
    "profileName": "Default"
  },
  "indexState": {
    "freshness": "notChecked",
    "freshnessVerified": false,
    "indexedFingerprint": "...",
    "snapshot": {
      "schemaVersion": 1,
      "createdUtc": "2026-08-09T08:17:55.724534Z",
      "backendName": "...",
      "parsedPlugins": 252,
      "failedPlugins": 0
    }
  },
  "query": "New Vegas Medical Clinic",
  "count": 1,
  "results": [],
  "pagination": {
    "limit": 1,
    "hasMore": false
  },
  "warnings": []
}
```
