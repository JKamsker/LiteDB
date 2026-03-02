# Internal design & implementation details

## A) Refactor LiteDatabase construction to support builder/factory

File: `LiteDB/Client/Database/LiteDatabase.cs` (modify)

Add an internal constructor that can:

- [ ] accept an already-created plugin context
- [ ] optionally skip plugin initialization
- [ ] optionally release a factory/engine lease on dispose (for factory reuse)

Example signature (exact can vary, but must be decision-complete in behavior):

```csharp
internal LiteDatabase(
    ILiteEngine engine,
    bool disposeOnClose,
    BsonMapper mapper,
    DefaultPluginContext pluginContext,
    bool initializePlugins,
    IEnumerable<ILitePlugin> plugins,
    ConnectionString connectionStringForContext,
    IDisposable engineLease = null)
```

Rules:

- [ ] Public constructors continue to create `DefaultPluginContext` and call existing initialization path.
- [ ] Even when plugin initialization is skipped (`initializePlugins = false`), still apply the `pluginContext` to the engine via `IPluginHost.SetPluginContext(pluginContext)` (mirrors the existing `InitializePlugins(...)` behavior).
- [ ] Reuse-engine factory path:
  - [ ] Create one `DefaultPluginContext` + one internal "plugin host" `LiteDatabase` that runs plugin initialization once.
  - [ ] Returned `LiteDatabase` handles: `initializePlugins = false`, share the same `pluginContext`, and dispose `engineLease` exactly once when disposed (instead of disposing the engine).

Recommended lease/refcount model (deterministic):

- [ ] The factory holds one owning reference while it is not disposed.
- [ ] Each `CreateDatabase()` returns a handle with its own `engineLease` reference.
- [ ] Disposing the factory prevents new handles and releases the owning reference.
- [ ] Disposing the factory must not invalidate existing handles; they remain usable until disposed.
- [ ] The reused engine is disposed when the last reference is released (factory disposed + all handles disposed).
- [ ] The internal "plugin host" used for initialization must not release a lease (it should not own a reference).
- [ ] `CreateDatabase()` must be thread-safe: refcounts use `Interlocked`, and plugin initialization is guarded to run exactly once even under concurrent calls.
- [ ] Handle disposal uses `Interlocked.CompareExchange(ref _leaseReleased, 1, 0)` to ensure idempotency (NOT a simple boolean flag, which is not thread-safe under concurrent dispose).
- [ ] Handle disposal releases the lease only from `Dispose(true)`, never from a finalizer path (`Dispose(false)`).
- [ ] Factory dispose + concurrent `CreateDatabase()` race: `CreateDatabase()` does `Interlocked.Increment` first, then checks if factory is disposed; if disposed, decrements and throws `ObjectDisposedException`. This prevents the TOCTOU race where the factory releases its ref between the disposed-check and the refcount-increment.

Notes:

- [ ] **`ILitePlugin.Initialize` contract violation**: The Spatial plugin's `SpatialPluginRegistry.Attach(database, context)` stores the `LiteDatabase` reference in a `ConditionalWeakTable` and uses it for ongoing I/O. This makes factory reuse incompatible with the Spatial plugin. See Intention.md for resolution options (change `Initialize` signature or add per-handle hook).
- [ ] Engine/plugin context must be treated as a fixed pair in reuse mode (do not swap contexts on a reused engine instance).
- [ ] If the reused engine is `SharedEngine`, ensure mutex acquisition/release is reentrancy-safe for nested operations: every `WaitOne()` has a matching `ReleaseMutex()` (even on exceptions), and mutex release must not be conditional on whether an engine instance was created.
- [ ] `_plugins` field in `LiteEngine` (line 41) is not `volatile` and has no memory barrier. `SetPluginContext` must happen-before any concurrent engine operations. In factory mode this is naturally guaranteed (context is set during factory build, before any handle is issued). Document this as a requirement.
- [ ] **`Rebuild()` incompatibility**: `Rebuild()` calls `this.Close()` + `this.Open()`, which destroys and recreates engine internals. In factory mode, this breaks all shared handles. Solution: refuse `Rebuild()` on the engine when factory refcount > 1, or expose rebuild as a factory-level operation requiring exclusive access.

Also ensure the existing stream constructor's checkpoint override behavior remains identical:

- [ ] If `logStream == null` and stream is not `MemoryStream` and the stream is writable, force checkpoint size to 1 and restore on dispose.

---

## B) Missing-plugin safety semantics (host-controlled)

File: `LiteDB/Engine/Services/SnapShot.cs` (modify)

Current behavior (bugs to fix):

- [ ] Enforcement reads `DiagnosticPolicy.MissingBehavior` (plugin-controlled), not `LiteDatabaseOptions.MissingPluginBehavior` (host-controlled). Must change to host-controlled.
- [ ] `EvaluatePluginAssets()` only iterates `CollectionPage.GetPluginIndexes()` (metadata entries). Misses `IndexType != 0` indexes when metadata is missing/corrupt. Must also scan `CollectionIndex` entries.
- [x] `CollectionPage` parsing can throw `PLUGIN_REQUIRED` for legacy/corrupt plugin metadata markers before enforcement runs, which can bypass host policy and (for write snapshots) risks leaking collection locks. Snapshot construction must be exception-safe: any exception after acquiring a write lock (including during `CollectionService.Get(...)` / collection-page parsing) must dispose and release the lock. Plugin metadata parsing must be treated as diagnostic-only for safety decisions.
- [ ] `HandleMissingPluginAsset` only has two branches: `RefuseDatabase` → throw, everything else → warn and allow. No write-mode refusal exists. Must add `AllowIfSafe` write refusal.
- [ ] `_missingPluginWarnings` is a `static ConcurrentDictionary<string, byte>` keyed only by `pluginId` (process-global). Must be scoped by database identity.
- [ ] `DropCollection` silently skips cleanup when `strategy == null && index.IndexType != 0` (page leak). Must throw instead.

Updated behavior:

- [ ] Define "plugin-owned assets" as:
  - [ ] any `CollectionIndex` with `IndexType != 0` (authoritative), and/or
  - [ ] any plugin metadata entry (`CollectionPage.GetPluginIndexes()`) when readable (diagnostics only).
- [ ] Read enforcement mode from `LiteDatabaseOptions.MissingPluginBehavior` (propagated via `DefaultPluginContext` or engine settings, NOT from `IPluginDiagnosticPolicy`).
- [ ] `RefuseDatabase`: throw whenever plugin-owned assets are encountered (read or write snapshots).
- [ ] `AllowIfSafe`:
  - [ ] Read snapshot on an affected collection: warn once (per db identity + pluginId/indexType), allow.
  - [ ] Write snapshot (and DDL) touching an affected collection: throw `PLUGIN_REQUIRED` (prevents stale plugin indexes / inconsistency).

Implementation approach:

- [ ] In `EvaluatePluginAssets()`, iterate ALL `CollectionIndex` entries and check `IndexType != 0` as the primary indicator. Use metadata entries for pluginId enrichment / diagnostics.
- [ ] In `HandleMissingPluginAsset(...)`, branch on host-controlled `MissingPluginBehavior` and `_mode`:
  - [ ] `AllowIfSafe` only permits `LockMode.Read` snapshots.
- [ ] Do not treat a "loaded pluginId descriptor" as sufficient for write safety: any `IndexType != 0` index requires an installed `IIndexStrategy` for its persisted `IndexType` (`_plugins?.Indexes?.GetByType(index.IndexType)`), especially for write/DDL paths.
- [ ] Warning/warn-once cache: scope by database identity + pluginId (or `<unknown>:type:{indexType}` when pluginId is unknown). Use `ConditionalWeakTable` keyed by engine instance, or scope to `DefaultPluginContext` (which has the right lifetime) rather than a static dictionary. `$plugins` and validation-on-open must not populate this cache.
- [ ] When `pluginId` is unknown (for example, `IndexType != 0` but metadata is missing/corrupt), include `indexType` in diagnostics.
- [ ] Core warning text must be plugin-agnostic. Remove the hard-coded `"LiteDB.Vector"` message from `DefaultPluginDiagnosticPolicy`.
- [ ] Plugin metadata entries must be treated as diagnostics-only: legacy/corrupt entries must not prevent opening read snapshots on unaffected collections, and must not bypass host-controlled enforcement decisions.
- [ ] Required code change: `CollectionPage` metadata parsing must not throw on legacy/corrupt metadata markers; it must record per-entry parse errors for diagnostics (`errors[]`) and continue.
- [ ] Add defensive guard in `DropCollection`: if `strategy == null && index.IndexType != 0`, throw `PLUGIN_REQUIRED` (belt-and-suspenders; should never be reached if snapshot-level refusal works correctly).
- [ ] Note: `ForUpdate` queries open `LockMode.Write` snapshots (see `QueryExecutor.cs` line 91). The snapshot-level enforcement correctly catches these. Test plan must include a `ForUpdate` test case.

---

## C) Prevent core planner from treating plugin indexes as btree

File: `LiteDB/Engine/Query/QueryOptimization.cs` (modify)

**This is an existing bug, not just a new feature.**

Update `ChooseIndex(...)` to consider only `CollectionIndex` entries with `IndexType == 0` (btree). The simplest fix is adding `.Where(x => x.IndexType == 0)` at line 347 where the `indexes` array is fetched:

```csharp
var indexes = _snapshot.CollectionPage.GetCollectionIndexes()
    .Where(x => x.IndexType == 0)  // ADD THIS
    .ToArray();
```

This propagates to ALL downstream usages including:
- [ ] Lines 368-383: predicate matching (`ANY` and scalar index lookups)
- [ ] Lines 400-406: fallback `GroupBy`/`OrderBy`/preferred index selection

Plugin indexes are only selectable via plugin planning rules (e.g., vector's `VectorIndexPlanningRule`).

This must be unconditional: core btree planning must never treat plugin indexes as btree, regardless of missing-plugin mode.

---

## D) Opt-in "validate plugins on open"

Goal: if enabled, fail fast instead of waiting for first collection access.

Default: disabled unless explicitly enabled (preserve legacy behavior; strict mode can still fail later when the first affected collection is accessed).

Implementation:

- [ ] Store requested validation on the plugin context (so it works with SharedEngine recreating LiteEngine instances).
- [ ] Add an internal interface (example):
  - [ ] File: `LiteDB/Plugins/IPluginValidationState.cs` (new, internal)
  - [ ] `bool ValidatePluginAssetsOnOpen { get; set; }`
  - [ ] `bool PluginAssetsValidated { get; set; }`
- [ ] `DefaultPluginContext` implements it and initializes to false.

Where validation executes:

- [ ] File: `LiteDB/Engine/LiteEngine.cs` (modify `IPluginHost.SetPluginContext`)
  - [ ] After setting `_plugins`, if:
    - [ ] context implements `IPluginValidationState` and `ValidatePluginAssetsOnOpen == true` and `PluginAssetsValidated == false`
  - [ ] then run a *non-enforcing collection-page scan* (do not open per-collection snapshots by name):
    - [ ] iterate `_header.GetCollections()` in an `AutoTransaction(...)`
    - [ ] read each collection page as a raw `PageBuffer` (WAL-aware) by `pageId`:
      - [ ] do NOT use `Snapshot.GetPage<T>` / `BasePage.ReadPage(...)` for `PageType.Collection` because they instantiate `CollectionPage` and can throw before a fault-tolerant `TryParse` runs
      - [ ] add an internal helper (e.g., `Snapshot.ReadPageBuffer(uint pageId, out FileOrigin origin)` or equivalent) that returns the correct page version for the snapshot read version without constructing a page type (and always requires an explicit `buffer.Release()` by the caller)
    - [ ] **Fault-tolerant scanning**: use a lightweight scan result (not `CollectionPage`/`CollectionIndex`) that does NOT compile `BsonExpression` or require index metadata to be parseable. This scan must surface per-index/plugin-metadata errors as data (for `$plugins` / exception diagnostics).
    - [ ] **Buffer safety**: extract all needed data (index types, plugin metadata) into local variables from the scan result, then release the buffer explicitly. Do not rely on `snapshot.Clear()` to release raw buffers.
    - [ ] detect affected collections using:
      - [ ] `CollectionIndex.IndexType != 0` (authoritative), and/or
      - [ ] plugin index metadata entries when readable (diagnostics only)
    - [ ] record required pluginIds + affected collections + parse errors into the context for later diagnostics (and `$plugins`)
  - [ ] apply host policy to the scan result:
    - [ ] `RefuseDatabase`: throw (fail fast) if any affected collection is found where the required plugin is not loaded OR any required `IndexType != 0` lacks a registered `IIndexStrategy` (pluginId unknown/metadata errors become diagnostics, not the enforcement marker)
    - [ ] `AllowIfSafe`: do not throw; record diagnostics for `$plugins` / typed API. Do not warn here (warnings are emitted on first real affected-collection access) and do not populate the enforcement warn-once cache
  - [ ] set `PluginAssetsValidated = true` on the context.

Notes:

- [ ] `SetPluginContext` is called after `LiteEngine.Open()` completes (from `LiteDatabase.InitializePlugins`). `AutoTransaction` and `_monitor` are available at this point.
- [ ] Validation-on-open validates plugin-owned index requirements only; it does not attempt to prove that all documents/pages are readable without plugin-defined BSON/page support.
- [ ] `PluginAssetsValidated` caching is valid because plugin contexts are scoped to a single engine/database identity.
- [ ] Treat collection-page parse failures as "affected": under `RefuseDatabase`, fail fast with aggregated diagnostics; under `AllowIfSafe`, record the error and continue scanning.
- [ ] Any warnings (emitted on first real affected-collection access under `AllowIfSafe`) must use the same "database identity" scoping defined in `Intention.md`.
- [ ] If validation throws under `RefuseDatabase`, include the aggregated scan result in the thrown exception diagnostics (same data surfaced by `$plugins` / typed API), since the database may not be openable for introspection.
- [ ] If `SetPluginContext`/validation throws during construction (builder or constructors), dispose the just-created engine and release any file handles/mutexes (construction must be exception-safe; no leaked engines on failure paths).
- [ ] **SharedEngine exception safety (required code change)**: `SharedEngine.OpenDatabase()` must handle exceptions from `SetPluginContext` correctly. Currently, the catch block releases the mutex but does NOT set `_engine = null` or dispose the partially-constructed engine. Recommended fix pattern: use a local variable for engine construction, only assign to `_engine` after both construction AND `SetPluginContext` succeed:
  ```csharp
  var engine = new LiteEngine(_settings);
  try
  {
      if (_plugins != null)
          ((IPluginHost)engine).SetPluginContext(_plugins);
      _engine = engine; // assign only on success
      return true;
  }
  catch
  {
      engine.Dispose(); // dispose the failed engine
      throw; // re-throw; outer catch releases mutex
  }
  ```
  This avoids the double-dispose risk of assigning first and disposing on failure, and ensures `_engine` is never set to a broken instance.
- [ ] Under `SharedEngine`, validation runs when the underlying `LiteEngine` is created and `SetPluginContext` executes. `PluginAssetsValidated` persists on the context across engine recreations, so re-scans are avoided. However, this cached result is not cross-process authoritative (another process could modify the database between operations). This is documented as a limitation.
- [ ] Exception safety: callers must treat exceptions from `SetPluginContext` as open failures.

---

## E) $plugins system collection

Files:

- [ ] `LiteDB/Engine/SystemCollections/SysPlugins.cs` (new) OR add `SysPlugins()` method in `LiteEngine` partial
- [ ] `LiteDB/Engine/SystemCollections/Register.cs` (modify)

Output: one row per plugin key (`pluginId` when known; otherwise `type:{indexType}`), aggregated across user collections (stable schema).

```json
{
  "key": "LiteDB.Vector",
  "pluginId": "LiteDB.Vector",
  "collections": ["vectors", "docs"],
  "indexCount": 2,
  "loaded": true,
  "strategyAvailable": true,
  "errors": []
}
```

For unknown rows, use `pluginId = "<unknown>"` and `key = "type:{indexType}"` (and optionally include `indexTypeCounts` for diagnostics). If an orphan metadata entry cannot be attributed to any plugin key, surface it under a dedicated row (e.g., `key = "orphan-metadata"`).

Key requirements:

- [ ] Must work even when missing-plugin behavior is strict.
- [ ] Must avoid opening per-collection snapshots that would trigger missing-plugin enforcement or populate warning caches.

Implementation approach:

- [ ] Open a single read snapshot against `"$"` (proven pattern: `SysDump` already does this at `SysDump.cs` line 36).
- [ ] Scan collections from `_header.GetCollections()` and read each collection page **buffer** by `pageId` via a raw-buffer read helper (WAL-aware; do not instantiate `CollectionPage` via the normal snapshot page factory path).
- [ ] **Fault-tolerant scanning**: the `$plugins` implementation must NOT use the `CollectionPage` constructor (it throws `LiteException(PLUGIN_REQUIRED)` on legacy metadata format). Use the same lightweight scan helper as validation-on-open, and surface errors as data (`errors[]`) rather than exceptions.
- [ ] Buffer/memory hygiene: extract all data from the scan result into local variables BEFORE releasing the `PageBuffer`. Do not rely on `snapshot.Clear()` to release raw buffers.
- [ ] Aggregate rows by plugin key: `key = pluginId` when readable; otherwise `key = $"type:{indexType}"`. Keep schema stable and avoid collapsing distinct unknown index types into a single `<unknown>` row.
- [ ] Compute `loaded` by checking `_plugins?.CustomIndexes?.Registered` contains a descriptor with matching `PluginId`.
- [ ] Compute `strategyAvailable` by checking that an `IIndexStrategy` exists for each persisted `IndexType` encountered for the row (protects against “pluginId registered but persisted IndexType not supported” version mismatches).

Prefer implementing a typed programmatic API first, then layering `$plugins` on top:

```csharp
IReadOnlyList<PluginRequirement> LiteDatabase.GetPluginRequirements();
```

Implementation note: to avoid drift, implement a single internal scanner (e.g., `PluginRequirementScanner`) used by:
- [ ] the typed API
- [ ] `$plugins`
- [ ] validation-on-open

This bypasses the query/snapshot/enforcement pipeline entirely and is more discoverable, testable, and type-safe. The `$plugins` system collection can be a thin wrapper over this API.

Registration:

- [ ] Add `this.RegisterSystemCollection("$plugins", () => this.SysPlugins());`

---

## F) Rebuild: opt-in dropping orphaned plugin indexes

Files:

- [ ] `LiteDB/Engine/Structures/RebuildOptions.cs` (modify)
- [ ] `LiteDB/Engine/Engine/Rebuild.cs` (modify)
- [ ] `LiteDB/Engine/Services/RebuildService.cs` (modify)
- [ ] `LiteDB/Engine/FileReader/FileReaderV8.cs` (modify)

### Behavior

- [ ] Default: unchanged. Missing plugin metadata/strategy during rebuild throws `PLUGIN_REQUIRED`.
- [ ] If `DropOrphanedPluginIndexes == true`:
  - [ ] Rebuild proceeds even if plugin-owned indexes cannot be recreated.
  - [ ] Those indexes are omitted from the rebuilt database.
  - [ ] Emit warnings and record dropped indexes into the rebuild report.
  - [ ] This is **index salvage only**: must still fail if documents cannot be decoded.
  - [ ] Require audit trail: `DropOrphanedPluginIndexes=true` with `IncludeErrorReport=false` throws `InvalidOperationException`.

### Required plumbing

1. [ ] `LiteEngine.Rebuild(RebuildOptions options)`:
   - [ ] Perform a rebuild-specific preflight *before* `this.Close()` that checks ALL `CollectionIndex` entries for `IndexType != 0` (not just plugin metadata entries). This must run regardless of `PluginMissingBehavior` (rebuild is always DDL-strict). Only bypass when `DropOrphanedPluginIndexes == true`.
   - [ ] Current bug: `EnsurePluginAssetsAllowed()` skips the check for non-`RefuseDatabase` modes (line 44-47 of `Rebuild.cs`: `if (behavior != PluginMissingBehavior.RefuseDatabase) return;`). This must be fixed: the preflight must always run.

2. [ ] `RebuildService` passes `options.DropOrphanedPluginIndexes` into `FileReaderV8`.

3. [ ] `FileReaderV8.LoadIndexes()`:
   - [ ] Must always capture `pluginId` + raw metadata bytes even when metadata registry is missing.
   - [ ] Treat any index with `IndexType != 0` as plugin-owned even if metadata cannot be parsed.
   - [ ] When `allowOrphanedPluginIndexes == true`, do not throw `CreateMetadataSerializerException`.
   - [ ] Populate `IndexInfo.PluginId` + `IndexInfo.PluginMetadata` from raw plugin index map.

4. [ ] `LiteEngine.RebuildContent(...)` must accept `RebuildOptions` for per-index decisions.

5. [ ] `TryRebuildPluginIndex(collection, index, options)`:
   - [ ] Define "plugin-owned index" as "has plugin metadata and pluginId" OR "`IndexType != 0`" (current code returns `false` when `PluginMetadata == null`, causing plugin indexes without metadata to be rebuilt as btree -- this is a **current data corruption bug**).
   - [ ] If plugin-owned and cannot be rebuilt:
     - [ ] `DropOrphanedPluginIndexes == true`: record warning, return true (handled; skip).
     - [ ] else: throw.

6. [ ] `FileReaderV8.GetDocuments()`:
   - [ ] **Current bug**: `FileReaderV8.Open()` has a generic `catch (Exception)` (line 93) that calls `HandleError` and swallows the exception. This means `PLUGIN_REQUIRED` errors during `LoadIndexes()` are silently swallowed, and recovery proceeds with partial data. **Fix**: re-throw `LiteException` with `PLUGIN_REQUIRED` error code (do not swallow it).
   - [ ] Must treat missing-plugin decode as fatal by default and abort rebuild.
   - [ ] **Known gap**: `BufferReader` does not currently throw a specific exception type for unknown BSON type codes. To distinguish "plugin BSON decode failure" from "general corruption", either add a `PluginBsonTypeRequiredException` to `BufferReader`, or pre-check the BSON stream.

### Safety defaults

- [ ] `Recovery()` (auto rebuild on invalid state) does not enable dropping indexes and does not allow document skipping; remains strict.
- [ ] Ensure rebuild failure does not leave the `LiteEngine` instance permanently closed/disposed (use `try/finally` around close/reopen/rename operations).

---

## G) Registry freezing (new)

File: `LiteDB/Plugins/DefaultPluginContext.cs` (modify)

Add a `Freeze()` method that makes all registries read-only:

```csharp
public void Freeze()
{
    _frozen = true;
    // each registry's Register() method checks _frozen and throws InvalidOperationException
}
```

Called after all `ILitePlugin.Initialize` calls complete. `Freeze()` is called in ALL modes (`Build()` and `BuildFactory()`), not just factory mode, to enforce a uniform contract: `Initialize` is the only place to register, period. If any existing plugin does lazy registration after initialization, it must be refactored.

Also make `DiagnosticPolicy` setter check `_frozen` state (currently `SetDiagnosticPolicy` is public and unsynchronized -- a race hazard when the context is shared across handles). The `DiagnosticPolicy` getter should use `volatile` or equivalent memory barrier for reads in factory mode, since `AllowIfSafe` enforcement reads this from multiple threads.

---

## H) Files summary (expected touch points)

### New

- [ ] `LiteDB/Client/Database/LiteDatabaseBuilder.cs`
- [ ] `LiteDB/Client/Database/ILiteDatabaseFactory.cs`
- [ ] `LiteDB/Client/Database/LiteDatabaseFactory.cs`
- [ ] `LiteDB/Engine/SystemCollections/SysPlugins.cs` (or equivalent partial method)
- [ ] `LiteDB/Engine/Services/PluginRequirementScanner.cs` (internal shared scanner for typed API, `$plugins`, and validation-on-open)
- [ ] `LiteDB/Plugins/IPluginValidationState.cs` (internal helper interface)

### Modify

- [ ] `LiteDB/Client/Database/LiteDatabase.cs`
- [ ] `LiteDB/Client/Database/LiteDatabaseOptions.cs`
- [ ] `LiteDB/Client/Shared/SharedEngine.cs` (exception safety for `SetPluginContext`; dispose/null engine on failure; `_transactionRunning` thread-safety if factory-shared)
- [ ] `LiteDB/Plugins/DefaultPluginContext.cs` (store validation flags; host policy; `Freeze()`)
- [ ] `LiteDB/Plugins/ILitePlugin.cs` (resolve `Initialize` signature -- option a or b)
- [ ] `LiteDB/Plugins/PluginDiagnosticPolicy.cs` (remove hard-coded Vector message; deprecate `MissingBehavior`)
- [ ] `LiteDB/Engine/Services/SnapShot.cs` (write-mode refusal; `IndexType != 0` scanning; db-scoped cache; raw page-buffer helper for `$plugins`/validation scans; `DropCollection` guard)
- [ ] `LiteDB/Engine/Query/QueryOptimization.cs` (filter `IndexType == 0` only)
- [ ] `LiteDB/Engine/SystemCollections/Register.cs` (register `$plugins`)
- [ ] `LiteDB/Engine/Structures/RebuildOptions.cs`
- [ ] `LiteDB/Engine/Engine/Rebuild.cs` (preflight always runs; `TryRebuildPluginIndex` checks `IndexType`)
- [ ] `LiteDB/Engine/Services/RebuildService.cs`
- [ ] `LiteDB/Engine/FileReader/FileReaderV8.cs` (don't swallow `PLUGIN_REQUIRED`; `LoadIndexes` `IndexType` handling)
- [ ] `LiteDB/Engine/Pages/CollectionPage.cs` (add a fault-tolerant scan helper for `$plugins`/validation that does not throw on legacy/corrupt metadata and does not compile `BsonExpression`)
- [ ] `LiteDB/Engine/LiteEngine.cs` (`SetPluginContext` + validation-on-open; `_plugins` field documentation)
- [ ] (Spatial plugin): fix `SpatialPluginRegistry.Attach` to not capture `LiteDatabase`
