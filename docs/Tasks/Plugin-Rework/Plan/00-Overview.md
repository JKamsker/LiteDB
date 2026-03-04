# Overview

## Summary

Introduce a fluent `LiteDatabaseBuilder` and an `ILiteDatabaseFactory` that reuses a single in-process engine/context across multiple `CreateDatabase()` calls. Strengthen safety by:

- [x] Making missing-plugin behavior host-controlled (default stays strict).
- [x] Adding "validate plugin-owned index requirements on open" (opt-in; reads collection pages and detects plugin-owned indexes via `IndexType != 0`; plugin metadata entries improve diagnostics; does not rely on a new header marker being present).
- [x] Adding a `$plugins` system collection for plugin requirement introspection.
- [x] Adding an explicit rebuild opt-in to drop orphaned plugin indexes when plugins are missing.
- [x] Fixing existing bugs: core planner selecting plugin indexes as btree; `Recovery()` silently swallowing plugin errors; `DropCollection` silently leaking plugin index pages.

This is additive: keep all existing `LiteDatabase` constructors working. Defaults remain strict; any code relying on `IPluginDiagnosticPolicy.MissingBehavior` to relax enforcement must migrate to `LiteDatabaseOptions.MissingPluginBehavior`.

---

## Goals / Non-goals

### Goals

- [x] Fluent, composable initialization (`UsePlugin`, `UseFile`, `UseInMemory`, …).
- [x] `Build()` for single-use databases; `BuildFactory()` for shared-engine factories (one engine + one plugin context reused; ref-counted handles).
- [x] Safety-first plugin handling:
  - [x] Default remains `RefuseDatabase` (current behavior). Optional validation-on-open provides fail-fast failure in strict mode.
  - [x] Optional `AllowIfSafe` mode: allow reads but prevent writes/DDL when plugin-owned indexes are present and the plugin is missing.
- [x] Fix core planner to never select `IndexType != 0` indexes (existing bug).
- [x] Explicit, opt-in rebuild behavior to prevent accidental index loss.
- [x] Introspection: `$plugins` system collection.
- [x] Address `ILitePlugin.Initialize` contract: plugins must not capture the passed `LiteDatabase` or perform I/O during initialization. Resolve the incompatibility with existing plugins (Spatial plugin captures `LiteDatabase`).

### Non-goals (for this iteration)

- Persisting a full per-plugin manifest (versions, per-asset criticality, etc.).
- Making plugin-defined BSON types readable without the plugin (compat shims).
- General-purpose plugin page scanning/introspection beyond what can be derived from collection metadata/indexes.
- Plugin version mismatch detection (detecting "wrong version of plugin" vs "missing plugin").

## Phasing (recommended delivery order)

1. **Phase 1 -- Safety fixes** (standalone, no builder/factory needed):
   - [x] Fix `QueryOptimization.ChooseIndex` to filter `IndexType == 0` only.
   - [x] Add `MissingPluginBehavior` and `ValidatePluginsOnOpen` to `LiteDatabaseOptions`.
   - [x] Update `EvaluatePluginAssets` to scan `IndexType != 0` (not just metadata entries).
   - [x] Add write-mode refusal under `AllowIfSafe` in snapshot creation.
   - [x] Fix `FileReaderV8.Open()` catch-all to not swallow `PLUGIN_REQUIRED`.
   - [x] Fix `DropCollection` to throw on null strategy with `IndexType != 0`.
   - [x] Move enforcement from `IPluginDiagnosticPolicy.MissingBehavior` to `LiteDatabaseOptions.MissingPluginBehavior`.
   - [x] Deprecate `IPluginDiagnosticPolicy.MissingBehavior` (kept for compatibility; ignored for enforcement).
   - [x] Remove hard-coded Vector-specific message from `DefaultPluginDiagnosticPolicy`.
   - [x] Scope `_missingPluginWarnings` cache by database identity.
   - [x] Add `$plugins` system collection.
   - [x] Add rebuild `DropOrphanedPluginIndexes` option.

2. **Phase 2 -- Builder** (fluent API, no factory):
   - [x] `LiteDatabaseBuilder` with `Build()` → `ILiteDatabase`.
   - [x] All configuration methods (`UseFile`, `UsePlugin`, `WithMapper`, etc.).
   - [x] Builder validation (incompatible combinations throw at build time).

3. **Phase 3 -- Factory** (shared-engine, ref-counting):
   - [x] `BuildFactory()` → `ILiteDatabaseFactory`.
   - [x] Ref-counted handle/lease model.
   - [x] Resolve `ILitePlugin.Initialize` contract (option a or b from Intention.md).
   - [x] Registry freezing after initialization.
   - [x] Fix `SharedEngine` exception safety for `SetPluginContext`.
   - [x] Handle `Rebuild()` incompatibility with factory mode.
