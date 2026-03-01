
# Plugin Rework: Intention

## Safety (non-negotiable)

- Plugins must not break or corrupt existing databases (including databases created before vector/spatial work).
- No silent data loss or inconsistency: if an operation cannot be proven safe without a plugin, it must be refused by default.
- Missing-plugin handling is **host-controlled** and **defaults to strict**.

## Definitions (this iteration)

- **Plugin-owned index**: any `CollectionIndex` with `IndexType != 0` (authoritative on-disk marker).
- **Plugin index metadata entry**: persisted mapping (index name -> pluginId + metadata bytes). Improves diagnostics/rebuild, but may be missing/corrupt/legacy.
- **Affected collection**: any collection that contains a plugin-owned index and/or a plugin index metadata entry.
- **Loaded plugin**: `loaded=true` iff the current plugin context has a registered custom-index descriptor for that `pluginId`.

Note: plugins may also introduce persisted dependencies beyond indexes (custom BSON types, custom page types). `AllowIfSafe` only controls safety around plugin-owned indexes and does not guarantee that every read will succeed without the plugin.

### Missing-plugin modes (host policy)

- `RefuseDatabase`: strict default. Without validation-on-open, construction may succeed and the first access that opens a snapshot for an affected collection fails; with validation-on-open enabled it fails as early as the engine is opened (construction for direct engines; first operation for `ConnectionType.Shared` / `SharedEngine`).
- `RefuseOperations` (refuse affected collections entirely): allow opening the database and using unaffected collections, but refuse any access (read/write/DDL) to affected collections.
- `AllowIfSafe`: allow read-only access to affected collections, but refuse any write/DDL that could mutate them or rely on plugin-owned indexes.

### When plugin-owned indexes exist but the plugin is missing

- Core must never plan against or use plugin-owned indexes; only btree indexes (`IndexType == 0`) are eligible for core planning.
- Under `AllowIfSafe`, only operations that acquire `LockMode.Read` snapshots are permitted on affected collections. Any `LockMode.Write` (including “for update”), all writes, and all DDL that would touch an affected collection must be refused (`PLUGIN_REQUIRED`) to prevent stale plugin indexes and invariant violations.
- Destructive DDL (drop/rename collection, ensure/drop index, rebuild/recovery, etc.) must refuse before doing partial work; core must not delete/modify a collection while skipping plugin-owned cleanup (page leaks/corruption).
- If the database can be opened safely without the plugin, it should remain usable (at minimum for reads on unaffected collections and for introspection).

### Rebuild / recovery

- Default remains strict: rebuild/recovery must fail when affected collections exist and the required plugin is missing (including “unknown plugin” cases where pluginId cannot be recovered but `IndexType != 0` is present).
- Rebuild is DDL: it must throw `PLUGIN_REQUIRED` before closing/replacing files whenever plugin support is missing, regardless of `PluginMissingBehavior` (unless an explicit salvage option like “drop orphaned plugin indexes” is enabled).
- Optional “salvage index rebuild” mode (explicit user opt-in): rebuild may **drop** plugin-owned indexes that cannot be rebuilt without the plugin. This may remove constraints (uniqueness/invariants) and change query semantics/performance.
- Salvage rebuild must produce a durable report of what was dropped (collection, index name, pluginId or indexType, unique flag, expression, reason) and must never drop plugin-owned indexes implicitly.
- Salvage rebuild must not implicitly accept document loss: if any document cannot be decoded due to missing plugin BSON types/page factories, rebuild must fail by default (a separate explicit “allow data loss” option would be required for a “best-effort” salvage pass; not part of this intention).
- Automatic recovery remains strict and must never drop plugin-owned indexes or skip unreadable documents implicitly.

## On-disk truth vs. caches

- The authoritative on-disk marker for plugin-owned indexes is `CollectionIndex.IndexType != 0`. Plugin index metadata entries improve diagnostics/rebuild but are not required for safety decisions.
- Any header marker/flag is an optional optimization only; absence must never be treated as proof that no plugin assets exist.
- “Validate plugins on open” (opt-in) must work for legacy databases by scanning persisted collection metadata at least once (do not rely on a newly introduced header marker being present).
- “Validate plugins on open” validates index requirements only; it does not attempt to predict failures caused by other persisted plugin dependencies (custom BSON types/page factories).
- Any in-memory cache (validated / warned-once / etc.) is a performance optimization only; it must be scoped to a specific database identity (canonical absolute filename when file-backed; otherwise a per-engine instance identity) and is not cross-process authoritative under `ConnectionType.Shared`. Runtime enforcement must still re-check on access.

## Introspection

- Users must be able to discover required plugins even under strict missing-plugin policy.
- Provide a `$plugins` system collection (best-effort) that reports at least:
  - `pluginId`, `collections`, `indexCount`, and whether the plugin is currently loaded.
- `$plugins` output schema must be stable (one summary row per `pluginId`, including `<unknown>`), and may include additional fields like `errors` and `indexTypeCounts`.
- `$plugins` must be read-only and non-throwing: legacy/corrupt plugin metadata should be surfaced as data (error fields), not as exceptions, and scanning must continue.
- `$plugins` must not trigger missing-plugin enforcement or poison missing-plugin warning caches (no per-user-collection snapshot opens by collection name).
- When pluginId is unknown (e.g., `IndexType != 0` but metadata is missing/corrupt), `$plugins` must still surface the requirement (use `pluginId = "<unknown>"` and include `indexType` details).

## Modularity

- Plugins should be modular and self-contained, allowing easy addition/removal without requiring core changes.
- Core should contain only the minimal hooks required for safety, metadata persistence, and routing (planner guards, enforcement, rebuild plumbing).
- Core diagnostics must be plugin-agnostic; plugin packages/hosts provide plugin-specific guidance via diagnostic policies without weakening host enforcement.

## Builder + factory direction (additive)

The current `LiteDatabase` constructors remain supported; the builder/factory is an additive UX layer.

The goal is a fluent initialization path with explicit plugin and policy configuration:

```csharp
var factory = new LiteDatabaseBuilder()
    .WithServices(serviceProvider)
    .UsePlugin(sp => new MyPlugin(sp)) // DI-friendly when WithServices(...) is set
    .UseFile("my.db")
    .WithMissingPluginBehavior(PluginMissingBehavior.RefuseDatabase)
    .ValidatePluginsOnOpen()
    .BuildFactory(reuse: FactoryReuse.ReuseEngine);

using var db = factory.CreateDatabase();
```

Key constraints:

- Avoid naming collisions with existing `ConnectionType.Shared` / `SharedEngine`; “factory reuse” is an in-process lifetime concept, not the connection-string shared mutex mode.
- `FactoryReuse` (in-process lifetime sharing) is independent of `ConnectionType.Shared` (cross-process mutex); they can be combined.
- In factory reuse mode, plugins must be initialized exactly once per plugin context/engine pair; plugin initialization must be treated as **registration-only** and must not capture the `LiteDatabase` instance passed to `ILitePlugin.Initialize` (future work may add an explicit per-handle/session hook if needed). Plugins that need per-handle state must use `FactoryReuse.None` (or build factories per scope) until a per-handle hook exists.
- Plugin `Initialize(...)` must not perform I/O (no opening snapshots, creating collections, or reads/writes); it must only register descriptors/operators/rules into the context.
- In `FactoryReuse.ReuseEngine`, mapper + missing-plugin policy + validation-on-open are factory-level and consistent across all created handles.
- The factory owns the engine + plugin context; `CreateDatabase()` returns leases/handles. Handle disposal must be idempotent and must never release a lease from a finalizer (`Dispose(false)`).
- Disposing the factory prevents new handles but must not invalidate existing ones; the reused engine is disposed when the last lease is released (factory disposed + all handles disposed).
- Handles must outlive any readers/transactions they created (callers must dispose those first).
- In `FactoryReuse.ReuseEngine`, plugin factories are invoked once per factory; the `IServiceProvider` (and any captured services) must be thread-safe and must outlive the factory + all created handles. Avoid scoped-service assumptions unless the host builds factories per scope.
- In `FactoryReuse.ReuseEngine`, the mapper and plugin registries (`db.Services.*`) are shared across handles; treat them as immutable after factory build.
