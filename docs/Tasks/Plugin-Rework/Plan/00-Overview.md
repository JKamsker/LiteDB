# Overview

## Summary

Introduce a fluent `LiteDatabaseBuilder` and an `ILiteDatabaseFactory` that reuses a single in-process engine/context across multiple `CreateDatabase()` calls. Strengthen safety by:

- Making missing-plugin behavior host-controlled (default stays strict).
- Adding "validate plugin-owned index requirements on open" (defaults to `true` for `RefuseDatabase` mode; reads collection pages and detects plugin-owned indexes via `IndexType != 0`; plugin metadata entries improve diagnostics; does not rely on a new header marker being present).
- Adding a `$plugins` system collection for plugin requirement introspection.
- Adding an explicit rebuild opt-in to drop orphaned plugin indexes when plugins are missing.
- Fixing existing bugs: core planner selecting plugin indexes as btree; `Recovery()` silently swallowing plugin errors; `DropCollection` silently leaking plugin index pages.

This is additive: keep all existing `LiteDatabase` constructors working and behavior-compatible by default.

---

## Goals / Non-goals

### Goals

- Fluent, composable initialization (`UsePlugin`, `UseFile`, `UseInMemory`, …).
- `Build()` for single-use databases; `BuildFactory()` for shared-engine factories (one engine + one plugin context reused; ref-counted handles).
- Safety-first plugin handling:
  - Default remains `RefuseDatabase` (current behavior) with fail-fast validation on open.
  - Optional `AllowIfSafe` mode: allow reads but prevent writes/DDL when plugin-owned indexes are present and the plugin is missing.
- Fix core planner to never select `IndexType != 0` indexes (existing bug).
- Explicit, opt-in rebuild behavior to prevent accidental index loss.
- Introspection: `$plugins` system collection.
- Address `ILitePlugin.Initialize` contract: plugins must not capture the passed `LiteDatabase` or perform I/O during initialization. Resolve the incompatibility with existing plugins (Spatial plugin captures `LiteDatabase`).

### Non-goals (for this iteration)

- Persisting a full per-plugin manifest (versions, per-asset criticality, etc.).
- Making plugin-defined BSON types readable without the plugin (compat shims).
- General-purpose plugin page scanning/introspection beyond what can be derived from collection metadata/indexes.
- Plugin version mismatch detection (detecting "wrong version of plugin" vs "missing plugin").

## Phasing (recommended delivery order)

1. **Phase 1 -- Safety fixes** (standalone, no builder/factory needed):
   - Fix `QueryOptimization.ChooseIndex` to filter `IndexType == 0` only.
   - Add `MissingPluginBehavior` and `ValidatePluginsOnOpen` to `LiteDatabaseOptions`.
   - Update `EvaluatePluginAssets` to scan `IndexType != 0` (not just metadata entries).
   - Add write-mode refusal under `AllowIfSafe` in snapshot creation.
   - Fix `FileReaderV8.Open()` catch-all to not swallow `PLUGIN_REQUIRED`.
   - Fix `DropCollection` to throw on null strategy with `IndexType != 0`.
   - Move enforcement from `IPluginDiagnosticPolicy.MissingBehavior` to `LiteDatabaseOptions.MissingPluginBehavior`.
   - Deprecate/remove `IPluginDiagnosticPolicy.MissingBehavior`.
   - Remove hard-coded Vector-specific message from `DefaultPluginDiagnosticPolicy`.
   - Scope `_missingPluginWarnings` cache by database identity.
   - Add `$plugins` system collection.
   - Add rebuild `DropOrphanedPluginIndexes` option.

2. **Phase 2 -- Builder** (fluent API, no factory):
   - `LiteDatabaseBuilder` with `Build()` → `ILiteDatabase`.
   - All configuration methods (`UseFile`, `UsePlugin`, `WithMapper`, etc.).
   - Builder validation (incompatible combinations throw at build time).

3. **Phase 3 -- Factory** (shared-engine, ref-counting):
   - `BuildFactory()` → `ILiteDatabaseFactory`.
   - Ref-counted handle/lease model.
   - Resolve `ILitePlugin.Initialize` contract (option a or b from Intention.md).
   - Registry freezing after initialization.
   - Fix `SharedEngine` exception safety for `SetPluginContext`.
   - Handle `Rebuild()` incompatibility with factory mode.
