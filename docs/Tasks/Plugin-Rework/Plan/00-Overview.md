# Overview

## Summary

Introduce a fluent `LiteDatabaseBuilder` and an `ILiteDatabaseFactory` that can either (a) create fresh databases from captured configuration, or (b) share a single in-process engine/context across multiple `CreateDatabase()` calls. Strengthen safety by:

- Making missing-plugin behavior host-controlled (default stays strict).
- Persisting a header marker (`HAS_PLUGIN_INDEXES`) when plugin-owned indexes exist.
- Adding an opt-in “validate plugin assets on open” scan.
- Adding a `$plugins` system collection for plugin requirement introspection.
- Adding an explicit rebuild opt-in to drop orphaned plugin indexes when plugins are missing.

This is additive: keep all existing `LiteDatabase` constructors working and behavior-compatible by default.

---

## Goals / Non-goals

### Goals

- Fluent, composable initialization (`UsePlugin`, `UseFile`, `UseInMemory`, …).
- `BuildFactory()` that supports:
  - Config-only mode (new engine + new plugin context per database)
  - Shared-engine mode (one engine + one plugin context shared; ref-counted)
- Safety-first plugin handling:
  - Default remains `RefuseDatabase` (current behavior).
  - Optional “read-only safe access” mode: allow reads but prevent writes when plugin assets are present and the plugin is missing.
- Explicit, opt-in rebuild behavior to prevent accidental index loss.
- Introspection: `$plugins` system collection.

### Non-goals (for this iteration)

- Persisting a full per-plugin manifest (versions, per-asset criticality, etc.).
- Making plugin-defined BSON types readable without the plugin (compat shims).
- General-purpose plugin page scanning/introspection beyond what can be derived from collection metadata/indexes.

