# Plugin Rework: Task Tracker

Use this file as the “single pane of glass” checklist. The detailed design and rationale lives in:

- [x] `Plan.md` (entry point)
- [x] `Intention.md` (constraints + policy)
- [x] `Plan/00-Overview.md` (phasing)
- [x] `Plan/01-Public-API.md` (builder/factory API)
- [x] `Plan/02-Internal-Design.md` (engine/snapshot/rebuild details)
- [x] `Plan/03-Files-Summary.md` (touch points)
- [x] `Plan/04-Test-Plan.md` (coverage)
- [x] `Plan/05-Assumptions.md` (defaults)

## Phase 1 — Safety fixes

- [x] Fix core planner to never select plugin indexes (`IndexType != 0`).
- [x] Make missing-plugin enforcement host-controlled (`LiteDatabaseOptions.MissingPluginBehavior`).
- [x] `AllowIfSafe`: refuse all writes/DDL on affected collections.
- [x] Scan `CollectionIndex.IndexType != 0` for enforcement (metadata is diagnostic-only).
- [x] `$plugins` introspection (safe under strict mode; no enforcement cache poisoning).
- [x] Validation-on-open (opt-in; fault-tolerant scan; strict mode fails fast).
- [x] Rebuild/recovery: never swallow `PLUGIN_REQUIRED`; keep strict-by-default behavior.
- [x] Optional salvage: `DropOrphanedPluginIndexes` requires durable error report.

## Phase 2 — Builder

- [x] Implement `LiteDatabaseBuilder` + `Build()`.
- [x] Enforce builder validation rules (single-use; mutually exclusive data source).
- [x] Define plugin registration semantics (dedupe, ownership, disposal).

## Phase 3 — Factory

- [x] Implement `ILiteDatabaseFactory` + `BuildFactory()`.
- [x] Ref-counted handle/lease model (idempotent dispose; thread-safe `CreateDatabase()`).
- [x] Resolve `ILitePlugin.Initialize` factory-mode contract (signature change vs per-handle hook).
- [ ] Freeze plugin context registries after initialization in all modes.
- [ ] Refuse or re-scope `Rebuild()` in factory mode (exclusive access requirement).

## Tests / Verification

- [ ] Implement test infrastructure helpers (`TestTrackingPlugin`, seed/race/corruption helpers).
- [ ] Cover builder + factory lifecycle/concurrency cases.
- [ ] Cover missing-plugin behavior matrix (planner/read/write/DDL).
- [ ] Cover `$plugins` + validation-on-open diagnostics and cache scoping.
- [ ] Cover rebuild salvage option + strict recovery behavior.
- [ ] Run: `dotnet test LiteDB.sln --settings tests.runsettings`.
