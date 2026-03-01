
# Plugin Rework: Intention

## Safety (non-negotiable)

- Plugins must not break or corrupt existing databases (including databases created before vector/spatial work).
- No silent data loss or inconsistency: if an operation cannot be proven safe without a plugin, it must be refused by default.
- Missing-plugin handling is **host-controlled** and **defaults to strict**.

### Missing-plugin modes (host policy)

- `RefuseDatabase`: strict default. Without validation-on-open, construction may succeed and the first access that opens a snapshot for an affected collection fails; with validation-on-open enabled it fails fast during construction.
- `RefuseOperations` (refuse affected collections entirely): allow opening the database and using unaffected collections, but refuse any access (read/write/DDL) to collections that contain plugin-owned assets.
- `AllowIfSafe`: allow reads on affected collections, but refuse any writes/DDL that would touch those collections.

### When plugin-owned assets exist but the plugin is missing

- **Reads** may be allowed (opt-in), but core must never plan against or use plugin indexes; only btree indexes are eligible for core planning.
- **Writes and DDL** (insert/update/delete, ensure/drop index, drop/rename collection, etc.) that would touch a collection with plugin-owned assets must be refused to prevent stale plugin indexes and invariant violations.
- If the database can be opened safely without the plugin, it should remain usable (at minimum for reads and introspection).

### Rebuild / recovery

- Default remains strict: rebuild/recovery must fail when plugin-owned indexes exist and the required plugin is missing.
- Optional “salvage” mode (explicit user opt-in): rebuild may **drop** plugin-owned indexes that cannot be rebuilt without the plugin. This must produce a durable report of what was dropped and may change query semantics (for example, uniqueness/constraints and performance).
- Salvage rebuild must bypass any strict missing-plugin pre-scan that would otherwise throw before rebuild can run.
- Automatic recovery remains strict and must never drop plugin-owned indexes implicitly.

## On-disk truth vs. caches

- The source of truth for plugin-owned assets is **persisted collection metadata** (plugin index entries and/or `IndexType != 0` indexes), not in-memory state.
- Any header marker/flag is an optional optimization only; absence must never be treated as proof that no plugin assets exist.
- “Validate plugins on open” (opt-in) must still work for legacy databases by scanning persisted collection metadata at least once (do not rely on a newly introduced header marker being present).

## Introspection

- Users must be able to discover required plugins even under strict missing-plugin policy.
- Provide a `$plugins` system collection (best-effort) that reports at least:
  - `pluginId`, `collections`, `indexCount`, and whether the plugin is currently loaded.
- Introspection must be read-only and must not weaken enforcement or suppress warnings globally (for example, it must not poison any “warn once per pluginId” caches).
- `$plugins` must be non-throwing: legacy/corrupt plugin metadata should be surfaced as data (error fields), not as exceptions.

## Modularity

- Plugins should be modular and self-contained, allowing easy addition/removal without requiring core changes.
- Core should contain only the minimal hooks required for safety, metadata persistence, and routing (planner guards, enforcement, rebuild plumbing).

## Builder + factory direction (additive)

The current `LiteDatabase` constructors remain supported; the builder/factory is an additive UX layer.

The goal is a fluent initialization path with explicit plugin and policy configuration:

```csharp
var factory = new LiteDatabaseBuilder()
    .UsePlugin(sp => new MyPlugin(sp)) // DI-friendly plugin factory
    .UseFile("my.db")
    .WithMissingPluginBehavior(PluginMissingBehavior.RefuseDatabase)
    .ValidatePluginsOnOpen()
    .BuildFactory(reuse: FactoryReuse.ReuseEngine);

using var db = factory.CreateDatabase();
```

Key constraints:

- Avoid naming collisions with existing `ConnectionType.Shared` / `SharedEngine`; “factory reuse” is an in-process lifetime concept, not the connection-string shared mutex mode.
- In factory reuse mode, plugins must be initialized exactly once per plugin context/engine pair; plugin initialization must be treated as **registration-only** and must not capture the `LiteDatabase` instance passed to `ILitePlugin.Initialize` (future work may add an explicit per-session hook if needed).
- In `FactoryReuse.ReuseEngine`, mapper + missing-plugin policy + validation-on-open are factory-level and consistent across all created handles.
- The factory owns the engine + plugin context; `CreateDatabase()` returns leases/handles. Disposing a handle must be idempotent and must not dispose the shared engine while other handles are alive. Handles must outlive any readers/transactions they created (callers must dispose those first).
