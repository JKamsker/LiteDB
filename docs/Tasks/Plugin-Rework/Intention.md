
# Plugin Rework: Intention

## Checklist

- [ ] Fix core planner to never select plugin-owned indexes (`IndexType != 0`).
- [ ] Decide missing-plugin behavior surface (`RefuseDatabase` vs `AllowIfSafe`) and document defaults.
- [ ] Resolve `ILitePlugin.Initialize` factory-mode incompatibility (change signature vs add per-handle hook).
- [ ] Enforce `AllowIfSafe` safety: refuse all writes/DDL that touch affected collections.
- [ ] Implement `$plugins` and validation-on-open via fault-tolerant scanning (no `CollectionPage` constructor, no warn-cache poisoning).
- [ ] Keep rebuild/recovery strict by default; if salvage-drop is enabled, require an audit trail/report.

## Safety (non-negotiable)

- Plugins must not break or corrupt existing databases (including databases created before vector/spatial work).
- No silent data loss or inconsistency: if an operation cannot be proven safe without a plugin, it must be refused by default.
- Missing-plugin handling is **host-controlled** and **defaults to strict**.
- Core query planner must never select plugin-owned indexes (`IndexType != 0`) as btree candidates. This is an existing bug that must be fixed regardless of mode.

## Definitions (this iteration)

- **Plugin-owned index**: any `CollectionIndex` with `IndexType != 0` (authoritative on-disk marker). Enforcement MUST scan `IndexType` directly; plugin index metadata entries are diagnostic only.
- **Plugin index metadata entry**: persisted mapping (index name -> pluginId + metadata bytes). Improves diagnostics/rebuild, but may be missing/corrupt/legacy, or orphaned (metadata without a matching index).
- **Affected collection**: for safety enforcement, any collection that contains a plugin-owned index (`IndexType != 0`). For reporting (`$plugins` / validation), also include collections where plugin index metadata exists or is corrupt/unparseable.
- **Loaded plugin**: `loaded=true` iff the current plugin context has a registered custom-index descriptor for that `pluginId` (diagnostic only; write/DDL safety also requires an index strategy for the persisted `IndexType`).
- **Strategy available**: `strategyAvailable=true` iff an `IIndexStrategy` is registered for every observed `IndexType != 0` required by that plugin key (protects against “pluginId registered but persisted IndexType unsupported” mismatches).

Note: plugins may also introduce persisted dependencies beyond indexes (custom BSON types, custom page types). `AllowIfSafe` only controls safety around plugin-owned indexes and does not guarantee that every read will succeed without the plugin.

### Missing-plugin modes (host policy)

- `RefuseDatabase`: strict default. Without validation-on-open, construction may succeed and the first access that opens a snapshot for an affected collection fails; with validation-on-open enabled (opt-in) it fails as early as the engine is opened (construction for direct engines; first operation for `ConnectionType.Shared` / `SharedEngine`). `ValidatePluginsOnOpen` defaults to `false` to preserve legacy behavior; enable it for fail-fast behavior in production (leave it disabled if you need `$plugins` introspection on a missing-plugin database).
- `AllowIfSafe`: allow read-only access to affected collections, but refuse any write/DDL that could mutate them or rely on plugin-owned indexes. Allow opening the database and using unaffected collections normally.

Note: the public behavior surface is two modes (`RefuseDatabase` and `AllowIfSafe`). The legacy enum member `RefuseOperations` remains only for binary compatibility and is treated as `AllowIfSafe` at runtime; it does not provide a distinct “refuse reads on affected collections” mode in this iteration.

### When plugin-owned indexes exist but the plugin is missing

- Core must never plan against or use plugin-owned indexes; only btree indexes (`IndexType == 0`) are eligible for core planning. This includes all index selection paths: predicate matching, `OrderBy`, `GroupBy`, and preferred-index fallbacks.
- Under `AllowIfSafe`, only operations that acquire `LockMode.Read` snapshots are permitted on affected collections. Any `LockMode.Write` (including “for update”), all writes, and all DDL that would touch an affected collection must be refused (`PLUGIN_REQUIRED`) to prevent stale plugin indexes and invariant violations.
- Destructive DDL (drop/rename collection, ensure/drop index, rebuild/recovery, etc.) must refuse before doing partial work; core must not delete/modify a collection while skipping plugin-owned cleanup (page leaks/corruption). `DropCollection` must preflight plugin index cleanup before mutating delete lists / marking the collection page deleted, and must add a defensive guard: if `strategy == null && index.IndexType != 0`, throw rather than silently skipping cleanup (belt-and-suspenders against future regressions).
- If the database can be opened safely without the plugin, it should remain usable (at minimum for reads on unaffected collections and for introspection).
- Insert/Update/Delete paths that iterate `_plugins?.Indexes?.All` silently skip plugin index maintenance when the plugin is missing. This is acceptable only because `AllowIfSafe` refuses the write snapshot before these paths execute. The snapshot-level refusal is the primary safety gate; the silent skip is NOT a safe fallback.

### Rebuild / recovery

- Default remains strict: rebuild/recovery must fail when affected collections exist and the required plugin is missing (including “unknown plugin” cases where pluginId cannot be recovered but `IndexType != 0` is present).
- Rebuild is DDL: it must throw `PLUGIN_REQUIRED` before closing/replacing files whenever plugin support is missing, regardless of `PluginMissingBehavior` (unless an explicit salvage option like “drop orphaned plugin indexes” is enabled).
- Rebuild/recovery must treat `IndexType != 0` as plugin-owned even when plugin metadata is missing/unreadable; it must never attempt to rebuild such indexes as btree indexes (fail or require explicit salvage-drop).
- Optional “salvage index rebuild” mode (explicit user opt-in): rebuild may **drop** plugin-owned indexes that cannot be rebuilt without the plugin. This may remove constraints (uniqueness/invariants) and change query semantics/performance.
- Salvage rebuild must produce a durable report of what was dropped (collection, index name, pluginId (or `<unknown>`), indexType, unique flag, expression, reason) and must never drop plugin-owned indexes implicitly.
- Salvage rebuild must not implicitly accept document loss: if any document cannot be decoded due to missing plugin BSON types/page factories, rebuild must fail by default (a separate explicit “allow data loss” option would be required for a “best-effort” salvage pass; not part of this intention).
- Automatic recovery remains strict and must never drop plugin-owned indexes or skip unreadable documents implicitly.
- **Known bug**: `FileReaderV8.Open()` has a generic `catch (Exception ex)` that calls `HandleError` and swallows the exception (no re-throw). This means recovery currently silently continues with partial data when plugin-required exceptions occur. This must be fixed: `PLUGIN_REQUIRED` exceptions must not be swallowed by the catch-all in `FileReaderV8.Open()`.
- **Known gap**: There is no mechanism to distinguish "plugin BSON type decode failure" from "general corruption" during document reading in `FileReaderV8.GetDocuments()`. To enforce "fail if documents can't be decoded due to missing plugin", either `BufferReader` must throw a specific exception type for unknown BSON type codes, or a pre-check must be added.

## On-disk truth vs. caches

- The authoritative on-disk marker for plugin-owned indexes is `CollectionIndex.IndexType != 0`. Plugin index metadata entries improve diagnostics/rebuild but are not required for safety decisions.
- Any header marker/flag is an optional optimization only; absence must never be treated as proof that no plugin assets exist.
- “Validate plugins on open” (opt-in) must work for legacy databases by scanning persisted collection pages/metadata at least once (do not rely on a newly introduced header marker being present).
- “Validate plugins on open” validates index requirements only; it does not attempt to predict failures caused by other persisted plugin dependencies (custom BSON types/page factories).
- Any in-memory cache (validated / warned-once / etc.) is a performance optimization only; it must be scoped to a specific database identity (canonical absolute filename when file-backed; otherwise a per-engine instance identity) and is not cross-process authoritative under `ConnectionType.Shared`. Runtime enforcement must still re-check on access.

## Introspection

- Users must be able to discover required plugins even under strict missing-plugin policy.
- Provide a `$plugins` system collection (best-effort) that reports at least:
  - `key` (stable row key)
  - `pluginId` (`"<unknown>"` when not recoverable)
  - `collections` (array of collection names)
  - `indexCount` (int)
  - `loaded` (bool; `true` iff a custom-index descriptor is registered for `pluginId`)
  - `strategyAvailable` (bool; `true` iff an `IIndexStrategy` exists for every observed `IndexType != 0` under this `key`)
- `$plugins` output schema must be stable (one summary row per plugin key: `pluginId` when known; otherwise `type:{indexType}`), and may include additional fields like `errors` and `indexTypeCounts`.
- `$plugins` must be read-only and non-throwing: legacy/corrupt plugin metadata should be surfaced as data (error fields), not as exceptions, and scanning must continue.
- `$plugins` must not trigger missing-plugin enforcement or poison missing-plugin warning caches (no per-user-collection snapshot opens by collection name).
- Other system collections (for example, `$indexes`) may open per-collection snapshots and can be refused/throw under strict missing-plugin policies; `$plugins` is the supported safe alternative for plugin discovery.
- When pluginId is unknown (e.g., `IndexType != 0` but metadata is missing/corrupt), `$plugins` must still surface the requirement (use `pluginId = "<unknown>"`, `key = "type:{indexType}"`, and include `indexType` details).
- `$plugins` should surface orphan metadata entries (metadata without a matching index) as `errors[]` entries on the owning row; if an orphan entry cannot be attributed to any plugin key, surface it under a dedicated row (e.g., `key = "orphan-metadata"`).
- `$plugins` scanning is inherently O(#collections) and must not be treated as a polling surface; hosts should cache results at the application level if needed.
- If `ValidatePluginsOnOpen=true` in strict mode, construction/open can fail before `$plugins` is queryable; the thrown exception must include the same requirement diagnostics that `$plugins`/typed API would report.

## Modularity

- Plugins should be modular and self-contained, allowing easy addition/removal without requiring core changes.
- Core should contain only the minimal hooks required for safety, metadata persistence, and routing (planner guards, enforcement, rebuild plumbing).
- Core diagnostics must be plugin-agnostic; plugin packages/hosts provide plugin-specific guidance via diagnostic policies without weakening host enforcement.

## Builder + factory direction (additive)

The current `LiteDatabase` constructors remain supported; the builder/factory is an additive UX layer.

The goal is a fluent initialization path with explicit plugin and policy configuration:

```csharp
// Single database (no engine sharing)
using var db = new LiteDatabaseBuilder()
    .UsePlugin<MyPlugin>()
    .UseFile("my.db")
    .WithMissingPluginBehavior(PluginMissingBehavior.RefuseDatabase)
    .Build();

// Shared-engine factory (multiple handles to same engine)
using var factory = new LiteDatabaseBuilder()
    .WithServices(serviceProvider)
    .UsePlugin(sp => new MyPlugin(sp))
    .UseFile("my.db")
    .WithMissingPluginBehavior(PluginMissingBehavior.RefuseDatabase)
    .BuildFactory();

using var db1 = factory.CreateDatabase();
using var db2 = factory.CreateDatabase(); // shares engine with db1
```

Key constraints:

- `Build()` creates a single `ILiteDatabase` (no engine sharing). `BuildFactory()` always creates a shared-engine factory (ref-counted handles). There is no `FactoryReuse` enum; the distinction is `Build()` vs `BuildFactory()`.
- Avoid naming collisions with existing `ConnectionType.Shared` / `SharedEngine`; factory engine sharing is an in-process lifetime concept, not the connection-string shared mutex mode.
- Factory engine sharing is independent of `ConnectionType.Shared` (cross-process mutex); they can be combined.
- In factory mode, plugins must be initialized exactly once per plugin context/engine pair.
- **`ILitePlugin.Initialize` contract**: The current signature `Initialize(LiteDatabase, ILitePluginContext)` is problematic because the Spatial plugin (and potentially others) captures the `LiteDatabase` reference for ongoing I/O. This is incompatible with factory mode where the initialization database is an internal host, not the returned handles. **Resolution options (choose one during implementation)**:
  - (a) Change signature to `Initialize(ILitePluginContext context)` -- removes the temptation to capture; requires updating existing plugins (breaking change).
  - (b) Add a per-handle hook `ILitePlugin.OnHandleCreated(ILiteDatabase handle)` -- allows plugins to bind to each handle; more pragmatic for existing plugins.
  - (c) Keep current signature + document the constraint + add runtime validation that the passed instance is not stored (impractical to enforce).
  Option (a) is preferred if breaking changes are acceptable in this iteration; option (b) is the pragmatic alternative.
- Plugin `Initialize(...)` must not perform I/O (no opening snapshots, creating collections, or reads/writes); it must only register descriptors/operators/rules into the context. Existing violations (SpatialPlugin.Initialize calls `SpatialPluginRegistry.Attach` which stores the database) must be fixed.
- In factory mode, mapper + missing-plugin policy + validation-on-open are factory-level and consistent across all created handles.
- The factory owns the engine + plugin context; `CreateDatabase()` returns leases/handles. Handle disposal must be idempotent and must never release a lease from a finalizer (`Dispose(false)`). Use `Interlocked.CompareExchange` pattern for idempotent lease release (a simple boolean flag is not thread-safe under concurrent dispose).
- `CreateDatabase()` must be thread-safe: use `Interlocked.Increment` for refcount, then check if factory is disposed; if disposed, decrement and throw `ObjectDisposedException`.
- Disposing the factory prevents new handles but must not invalidate existing ones; the reused engine is disposed when the last lease is released (factory disposed + all handles disposed).
- `Rebuild()` is incompatible with factory mode (it closes/reopens the engine internally, breaking shared handles). In factory mode, `Rebuild()` must either be refused, or exposed as a factory-level operation that requires exclusive access (no active handles).
- Handles must outlive any readers/transactions they created (callers must dispose those first).
- In factory mode, plugin factories are invoked once per factory; the `IServiceProvider` (and any captured services) must be thread-safe and must outlive the factory + all created handles. Avoid scoped-service assumptions unless the host builds factories per scope.
- In factory mode, the mapper and plugin registries (`db.Services.*`) are shared across handles. After initialization, registries must be sealed/frozen (`Freeze()` or equivalent) so that post-init registration attempts throw `InvalidOperationException` rather than silently mutating shared state.
- Plugin disposal follows ownership conventions: instances created by factories (`UsePlugin(Func<ILitePlugin>)`) are disposed by the factory/database when the owning scope ends; instances passed directly (`UsePlugin(ILitePlugin)`) are not disposed (caller owns).
- `UsePlugin(ILitePlugin instance)` with `BuildFactory()`: the instance is used as-is by the factory (initialized once). Caller retains ownership of disposal.
- `UseFile(...)` + `BuildFactory()` + `ConnectionType.Direct` (default) is safe and recommended: the factory creates ONE engine (exclusive file access) with multiple in-process handles. `ConnectionType.Shared` is only needed when multiple processes access the same file.
- Builder validates incompatible combinations: `UseEngine(...)` + `WithConnectionType(...)` throws; `UseStream(...)` + `BuildFactory()` throws (stream cannot be shared); second data-source call throws (no “last call wins”).
