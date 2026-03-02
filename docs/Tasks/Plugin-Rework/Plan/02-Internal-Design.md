# Internal design & implementation details

## A) Refactor LiteDatabase construction to support builder/factory

File: `LiteDB/Client/Database/LiteDatabase.cs` (modify)

Add an internal constructor that can:

- [x] accept an already-created plugin context
- [x] optionally skip plugin initialization
- [x] optionally release a factory/engine lease on dispose (for factory reuse)

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

- [x] Public constructors continue to create `DefaultPluginContext` and call existing initialization path.
- [x] Even when plugin initialization is skipped (`initializePlugins = false`), still apply the `pluginContext` to the engine via `IPluginHost.SetPluginContext(pluginContext)` (mirrors the existing `InitializePlugins(...)` behavior).
- [x] Reuse-engine factory path:
  - [x] Create one `DefaultPluginContext` + one internal "plugin host" `LiteDatabase` that runs plugin initialization once.
  - [x] Returned `LiteDatabase` handles: `initializePlugins = false`, share the same `pluginContext`, and dispose `engineLease` exactly once when disposed (instead of disposing the engine).

Recommended lease/refcount model (deterministic):

- [x] The factory holds one owning reference while it is not disposed.
- [x] Each `CreateDatabase()` returns a handle with its own `engineLease` reference.
- [x] Disposing the factory prevents new handles and releases the owning reference.
- [x] Disposing the factory must not invalidate existing handles; they remain usable until disposed.
- [x] The reused engine is disposed when the last reference is released (factory disposed + all handles disposed).
- [x] The internal "plugin host" used for initialization must not release a lease (it should not own a reference).
- [x] `CreateDatabase()` must be thread-safe: refcounts use `Interlocked`, and plugin initialization is guarded to run exactly once even under concurrent calls.
- [x] Handle disposal uses `Interlocked.CompareExchange(ref _leaseReleased, 1, 0)` to ensure idempotency (NOT a simple boolean flag, which is not thread-safe under concurrent dispose).
- [x] Handle disposal releases the lease only from `Dispose(true)`, never from a finalizer path (`Dispose(false)`).
- [x] Factory dispose + concurrent `CreateDatabase()` race: `CreateDatabase()` does `Interlocked.Increment` first, then checks if factory is disposed; if disposed, decrements and throws `ObjectDisposedException`. This prevents the TOCTOU race where the factory releases its ref between the disposed-check and the refcount-increment.

Notes:

- [x] **`ILitePlugin.Initialize` contract violation**: The Spatial plugin's `SpatialPluginRegistry.Attach(database, context)` stores the `LiteDatabase` reference in a `ConditionalWeakTable` and uses it for ongoing I/O. This makes factory reuse incompatible with the Spatial plugin. See Intention.md for resolution options (change `Initialize` signature or add per-handle hook).
- [x] Engine/plugin context must be treated as a fixed pair in reuse mode (do not swap contexts on a reused engine instance).
- [x] If the reused engine is `SharedEngine`, ensure mutex acquisition/release is reentrancy-safe for nested operations: every `WaitOne()` has a matching `ReleaseMutex()` (even on exceptions), and mutex release must not be conditional on whether an engine instance was created.
- [x] `_plugins` field in `LiteEngine` (line 41) is not `volatile` and has no memory barrier. `SetPluginContext` must happen-before any concurrent engine operations. In factory mode this is naturally guaranteed (context is set during factory build, before any handle is issued). Document this as a requirement.
- [x] **`Rebuild()` incompatibility**: `Rebuild()` calls `this.Close()` + `this.Open()`, which destroys and recreates engine internals. In factory mode, this breaks all shared handles. Solution: refuse `Rebuild()` on the engine when factory refcount > 1, or expose rebuild as a factory-level operation requiring exclusive access.

Also ensure the existing stream constructor's checkpoint override behavior remains identical:

- [x] If `logStream == null` and stream is not `MemoryStream` and the stream is writable, force checkpoint size to 1 and restore on dispose.

---

## B) Missing-plugin safety semantics (host-controlled)

File: `LiteDB/Engine/Services/SnapShot.cs` (modify)

Current behavior (bugs to fix):

- [x] Enforcement reads `DiagnosticPolicy.MissingBehavior` (plugin-controlled), not `LiteDatabaseOptions.MissingPluginBehavior` (host-controlled). Must change to host-controlled.
- [x] `EvaluatePluginAssets()` only iterates `CollectionPage.GetPluginIndexes()` (metadata entries). Misses `IndexType != 0` indexes when metadata is missing/corrupt. Must also scan `CollectionIndex` entries.
- [x] `CollectionPage` parsing can throw `PLUGIN_REQUIRED` for legacy/corrupt plugin metadata markers before enforcement runs, which can bypass host policy and (for write snapshots) risks leaking collection locks. Snapshot construction must be exception-safe: any exception after acquiring a write lock (including during `CollectionService.Get(...)` / collection-page parsing) must dispose and release the lock. Plugin metadata parsing must be treated as diagnostic-only for safety decisions.
- [x] `HandleMissingPluginAsset` only has two branches: `RefuseDatabase` → throw, everything else → warn and allow. No write-mode refusal exists. Must add `AllowIfSafe` write refusal.
- [x] `_missingPluginWarnings` is a `static ConcurrentDictionary<string, byte>` keyed only by `pluginId` (process-global). Must be scoped by database identity.
- [x] `DropCollection` silently skips cleanup when `strategy == null && index.IndexType != 0` (page leak). Must throw instead.

Updated behavior:

- [x] Define "plugin-owned assets" as:
  - [x] any `CollectionIndex` with `IndexType != 0` (authoritative), and/or
  - [x] any plugin metadata entry (`CollectionPage.GetPluginIndexes()`) when readable (diagnostics only).
- [x] Read enforcement mode from `LiteDatabaseOptions.MissingPluginBehavior` (propagated via `DefaultPluginContext` or engine settings, NOT from `IPluginDiagnosticPolicy`).
- [x] `RefuseDatabase`: throw whenever plugin-owned assets are encountered (read or write snapshots).
- [x] `AllowIfSafe`:
  - [x] Read snapshot on an affected collection: warn once (per db identity + pluginId/indexType), allow.
  - [x] Write snapshot (and DDL) touching an affected collection: throw `PLUGIN_REQUIRED` (prevents stale plugin indexes / inconsistency).

Implementation approach:

- [x] In `EvaluatePluginAssets()`, iterate ALL `CollectionIndex` entries and check `IndexType != 0` as the primary indicator. Use metadata entries for pluginId enrichment / diagnostics.
- [x] In `HandleMissingPluginAsset(...)`, branch on host-controlled `MissingPluginBehavior` and `_mode`:
  - [x] `AllowIfSafe` only permits `LockMode.Read` snapshots.
- [x] Do not treat a "loaded pluginId descriptor" as sufficient for write safety: any `IndexType != 0` index requires an installed `IIndexStrategy` for its persisted `IndexType` (`_plugins?.Indexes?.GetByType(index.IndexType)`), especially for write/DDL paths.
- [x] Warning/warn-once cache: scope by database identity + pluginId (or `<unknown>:type:{indexType}` when pluginId is unknown). Use `ConditionalWeakTable` keyed by engine instance, or scope to `DefaultPluginContext` (which has the right lifetime) rather than a static dictionary. `$plugins` and validation-on-open must not populate this cache.
- [x] When `pluginId` is unknown (for example, `IndexType != 0` but metadata is missing/corrupt), include `indexType` in diagnostics.
- [x] Core warning text must be plugin-agnostic. Remove the hard-coded `"LiteDB.Vector"` message from `DefaultPluginDiagnosticPolicy`.
- [x] Plugin metadata entries must be treated as diagnostics-only: legacy/corrupt entries must not prevent opening read snapshots on unaffected collections, and must not bypass host-controlled enforcement decisions.
- [x] Required code change: `CollectionPage` metadata parsing must not throw on legacy/corrupt metadata markers; it must record per-entry parse errors for diagnostics (`errors[]`) and continue.
- [x] Add defensive guard in `DropCollection`: if `strategy == null && index.IndexType != 0`, throw `PLUGIN_REQUIRED` (belt-and-suspenders; should never be reached if snapshot-level refusal works correctly).
- [x] Note: `ForUpdate` queries open `LockMode.Write` snapshots (see `QueryExecutor.cs` line 91). The snapshot-level enforcement correctly catches these. Test plan must include a `ForUpdate` test case.

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
- [x] Lines 368-383: predicate matching (`ANY` and scalar index lookups)
- [x] Lines 400-406: fallback `GroupBy`/`OrderBy`/preferred index selection

Plugin indexes are only selectable via plugin planning rules (e.g., vector's `VectorIndexPlanningRule`).

This must be unconditional: core btree planning must never treat plugin indexes as btree, regardless of missing-plugin mode.

---

## D) Opt-in "validate plugins on open"

Goal: if enabled, fail fast instead of waiting for first collection access.

Default: disabled unless explicitly enabled (preserve legacy behavior; strict mode can still fail later when the first affected collection is accessed).

Implementation:

- [x] Store requested validation on the plugin context (so it works with SharedEngine recreating LiteEngine instances).
- [x] Implementation note: the final design stores this state directly on `DefaultPluginContext` (no separate interface/file), using `ValidatePluginsOnOpen`, `ValidationOnOpenRan`, `ValidationOnOpenEngineInstanceId`, `ValidationOnOpenDiagnostics`, and `RecordValidationOnOpen(...)`.

Where validation executes:

- [x] File: `LiteDB/Engine/LiteEngine.cs` (modify `IPluginHost.SetPluginContext`)
  - [x] After setting `_plugins`, if:
    - [x] context is `DefaultPluginContext` and `ValidatePluginsOnOpen == true` (and the scan has not yet run for the current engine instance id)
  - [x] then run a *non-enforcing collection-page scan* (do not open per-collection snapshots by name):
    - [x] iterate `_header.GetCollections()` in an `AutoTransaction(...)`
    - [x] read each collection page as a raw `PageBuffer` (WAL-aware) by `pageId`:
      - [x] do NOT use `Snapshot.GetPage<T>` / `BasePage.ReadPage(...)` for `PageType.Collection` because they instantiate `CollectionPage` and can throw before a fault-tolerant `TryParse` runs
      - [x] use an internal helper (implemented in `PluginRequirementScanner.ReadPageBuffer(...)`) that returns the correct page version for the snapshot read version without constructing a page type (and always requires an explicit `buffer.Release()` by the caller)
    - [x] **Fault-tolerant scanning**: use a lightweight scan result (not `CollectionPage`/`CollectionIndex`) that does NOT compile `BsonExpression` or require index metadata to be parseable. This scan must surface per-index/plugin-metadata errors as data (for `$plugins` / exception diagnostics).
    - [x] **Buffer safety**: extract all needed data (index types, plugin metadata) into local variables from the scan result, then release the buffer explicitly. Do not rely on `snapshot.Clear()` to release raw buffers.
    - [x] detect affected collections using:
      - [x] `CollectionIndex.IndexType != 0` (authoritative), and/or
      - [x] plugin index metadata entries when readable (diagnostics only)
    - [x] record required pluginIds + affected collections + parse errors into the context for later diagnostics (and `$plugins`)
  - [x] apply host policy to the scan result:
    - [x] `RefuseDatabase`: throw (fail fast) if any affected collection is found where the required plugin is not loaded OR any required `IndexType != 0` lacks a registered `IIndexStrategy` (pluginId unknown/metadata errors become diagnostics, not the enforcement marker)
    - [x] `AllowIfSafe`: do not throw; record diagnostics for `$plugins` / typed API. Do not warn here (warnings are emitted on first real affected-collection access) and do not populate the enforcement warn-once cache
  - [x] record scan results on the context via `RecordValidationOnOpen(...)` (sets the cached validation state).

Notes:

- [x] `SetPluginContext` is called after `LiteEngine.Open()` completes (from `LiteDatabase.InitializePlugins`). `AutoTransaction` and `_monitor` are available at this point.
- [x] Validation-on-open validates plugin-owned index requirements only; it does not attempt to prove that all documents/pages are readable without plugin-defined BSON/page support.
- [x] Validation-on-open caching is valid because plugin contexts are scoped to a single engine/database identity.
- [x] Treat collection-page parse failures as "affected": under `RefuseDatabase`, fail fast with aggregated diagnostics; under `AllowIfSafe`, record the error and continue scanning.
- [x] Any warnings (emitted on first real affected-collection access under `AllowIfSafe`) must use the same "database identity" scoping defined in `Intention.md`.
- [x] If validation throws under `RefuseDatabase`, include the aggregated scan result in the thrown exception diagnostics (same data surfaced by `$plugins` / typed API), since the database may not be openable for introspection.
- [x] If `SetPluginContext`/validation throws during construction (builder or constructors), dispose the just-created engine and release any file handles/mutexes (construction must be exception-safe; no leaked engines on failure paths).
- [x] **SharedEngine exception safety (required code change)**: `SharedEngine.OpenDatabase()` must handle exceptions from `SetPluginContext` correctly. Currently, the catch block releases the mutex but does NOT set `_engine = null` or dispose the partially-constructed engine. Recommended fix pattern: use a local variable for engine construction, only assign to `_engine` after both construction AND `SetPluginContext` succeed:
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
- [x] Under `SharedEngine`, validation runs when the underlying `LiteEngine` is created and `SetPluginContext` executes. The cached validation state persists on the context (keyed by engine instance id), so re-scans are avoided. However, this cached result is not cross-process authoritative (another process could modify the database between operations). This is documented as a limitation.
- [x] Exception safety: callers must treat exceptions from `SetPluginContext` as open failures.

---

## E) $plugins system collection

Files:

- [x] `LiteDB/Engine/SystemCollections/SysPlugins.cs` (new) OR add `SysPlugins()` method in `LiteEngine` partial
- [x] `LiteDB/Engine/SystemCollections/Register.cs` (modify)

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

- [x] Must work even when missing-plugin behavior is strict.
- [x] Must avoid opening per-collection snapshots that would trigger missing-plugin enforcement or populate warning caches.

Implementation approach:

- [x] Open a single read snapshot against `"$"` (proven pattern: `SysDump` already does this at `SysDump.cs` line 36).
- [x] Scan collections from `_header.GetCollections()` and read each collection page **buffer** by `pageId` via a raw-buffer read helper (WAL-aware; do not instantiate `CollectionPage` via the normal snapshot page factory path).
- [x] **Fault-tolerant scanning**: the `$plugins` implementation must NOT use the `CollectionPage` constructor (it throws `LiteException(PLUGIN_REQUIRED)` on legacy metadata format). Use the same lightweight scan helper as validation-on-open, and surface errors as data (`errors[]`) rather than exceptions.
- [x] Buffer/memory hygiene: extract all data from the scan result into local variables BEFORE releasing the `PageBuffer`. Do not rely on `snapshot.Clear()` to release raw buffers.
- [x] Aggregate rows by plugin key: `key = pluginId` when readable; otherwise `key = $"type:{indexType}"`. Keep schema stable and avoid collapsing distinct unknown index types into a single `<unknown>` row.
- [x] Compute `loaded` by checking `_plugins?.CustomIndexes?.Registered` contains a descriptor with matching `PluginId`.
- [x] Compute `strategyAvailable` by checking that an `IIndexStrategy` exists for each persisted `IndexType` encountered for the row (protects against “pluginId registered but persisted IndexType not supported” version mismatches).

Prefer implementing a typed programmatic API first, then layering `$plugins` on top:

```csharp
IReadOnlyList<PluginRequirement> LiteDatabase.GetPluginRequirements();
```

Implementation note: to avoid drift, implement a single internal scanner (e.g., `PluginRequirementScanner`) used by:
- [x] the typed API
- [x] `$plugins`
- [x] validation-on-open

This bypasses the query/snapshot/enforcement pipeline entirely and is more discoverable, testable, and type-safe. The `$plugins` system collection can be a thin wrapper over this API.

Registration:

- [x] Add `this.RegisterSystemCollection("$plugins", () => this.SysPlugins());`

---

## F) Rebuild: opt-in dropping orphaned plugin indexes

Files:

- [x] `LiteDB/Engine/Structures/RebuildOptions.cs` (modify)
- [x] `LiteDB/Engine/Engine/Rebuild.cs` (modify)
- [x] `LiteDB/Engine/Services/RebuildService.cs` (modify)
- [x] `LiteDB/Engine/FileReader/FileReaderV8.cs` (modify)

### Behavior

- [x] Default: unchanged. Missing plugin metadata/strategy during rebuild throws `PLUGIN_REQUIRED`.
- [x] If `DropOrphanedPluginIndexes == true`:
  - [x] Rebuild proceeds even if plugin-owned indexes cannot be recreated.
  - [x] Those indexes are omitted from the rebuilt database.
  - [x] Emit warnings and record dropped indexes into the rebuild report.
  - [x] This is **index salvage only**: must still fail if documents cannot be decoded.
  - [x] Require audit trail: `DropOrphanedPluginIndexes=true` with `IncludeErrorReport=false` throws `InvalidOperationException`.

### Required plumbing

1. [x] `LiteEngine.Rebuild(RebuildOptions options)`:
   - [x] Perform a rebuild-specific preflight *before* `this.Close()` that checks ALL `CollectionIndex` entries for `IndexType != 0` (not just plugin metadata entries). This must run regardless of `PluginMissingBehavior` (rebuild is always DDL-strict). Only bypass when `DropOrphanedPluginIndexes == true`.
   - [x] Current bug: `EnsurePluginAssetsAllowed()` skips the check for non-`RefuseDatabase` modes (line 44-47 of `Rebuild.cs`: `if (behavior != PluginMissingBehavior.RefuseDatabase) return;`). This must be fixed: the preflight must always run.

2. [x] `RebuildService` passes `options.DropOrphanedPluginIndexes` into `FileReaderV8`.

3. [x] `FileReaderV8.LoadIndexes()`:
   - [x] Must always capture `pluginId` + raw metadata bytes even when metadata registry is missing.
   - [x] Treat any index with `IndexType != 0` as plugin-owned even if metadata cannot be parsed.
   - [x] When `allowOrphanedPluginIndexes == true`, do not throw `CreateMetadataSerializerException`.
   - [x] Populate `IndexInfo.PluginId` + `IndexInfo.PluginMetadata` from raw plugin index map.

4. [x] `LiteEngine.RebuildContent(...)` must accept `RebuildOptions` for per-index decisions.

5. [x] `TryRebuildPluginIndex(collection, index, options)`:
   - [x] Define "plugin-owned index" as "has plugin metadata and pluginId" OR "`IndexType != 0`" (current code returns `false` when `PluginMetadata == null`, causing plugin indexes without metadata to be rebuilt as btree -- this is a **current data corruption bug**).
   - [x] If plugin-owned and cannot be rebuilt:
     - [x] `DropOrphanedPluginIndexes == true`: record warning, return true (handled; skip).
     - [x] else: throw.

6. [x] `FileReaderV8.GetDocuments()`:
   - [x] **Current bug**: `FileReaderV8.Open()` has a generic `catch (Exception)` (line 93) that calls `HandleError` and swallows the exception. This means `PLUGIN_REQUIRED` errors during `LoadIndexes()` are silently swallowed, and recovery proceeds with partial data. **Fix**: re-throw `LiteException` with `PLUGIN_REQUIRED` error code (do not swallow it).
   - [x] Must treat missing-plugin decode as fatal by default and abort rebuild.
   - [x] **Known gap**: `BufferReader` does not currently throw a specific exception type for unknown BSON type codes. To distinguish "plugin BSON decode failure" from "general corruption", either add a `PluginBsonTypeRequiredException` to `BufferReader`, or pre-check the BSON stream.

### Safety defaults

- [x] `Recovery()` (auto rebuild on invalid state) does not enable dropping indexes and does not allow document skipping; remains strict.
- [x] Ensure rebuild failure does not leave the `LiteEngine` instance permanently closed/disposed (use `try/finally` around close/reopen/rename operations).

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

- [x] `LiteDB/Client/Database/LiteDatabaseBuilder.cs`
- [x] `LiteDB/Client/Database/ILiteDatabaseFactory.cs`
- [x] `LiteDB/Client/Database/LiteDatabaseFactory.cs`
- [x] `LiteDB/Engine/SystemCollections/SysPlugins.cs` (or equivalent partial method)
- [x] `LiteDB/Engine/Services/PluginRequirementScanner.cs` (internal shared scanner for typed API, `$plugins`, and validation-on-open)

Note: the initial design called for a dedicated `IPluginValidationState` interface, but the final implementation stores validation-on-open state directly on `DefaultPluginContext` (`ValidationOnOpen*` + `RecordValidationOnOpen(...)`).

### Modify

- [x] `LiteDB/Client/Database/LiteDatabase.cs`
- [x] `LiteDB/Client/Database/LiteDatabaseOptions.cs`
- [x] `LiteDB/Client/Shared/SharedEngine.cs` (exception safety for `SetPluginContext`; dispose/null engine on failure; `_transactionRunning` thread-safety if factory-shared)
- [x] `LiteDB/Plugins/DefaultPluginContext.cs` (store validation flags; host policy; `Freeze()`)
- [x] `LiteDB/Plugins/ILitePlugin.cs` (resolve `Initialize` signature -- option a or b)
- [x] `LiteDB/Plugins/PluginDiagnosticPolicy.cs` (remove hard-coded Vector message; deprecate `MissingBehavior`)
- [x] `LiteDB/Engine/Services/SnapShot.cs` (write-mode refusal; `IndexType != 0` scanning; db-scoped cache; `DropCollection` guard)
- [x] `LiteDB/Engine/Query/QueryOptimization.cs` (filter `IndexType == 0` only)
- [x] `LiteDB/Engine/SystemCollections/Register.cs` (register `$plugins`)
- [x] `LiteDB/Engine/Structures/RebuildOptions.cs`
- [x] `LiteDB/Engine/Engine/Rebuild.cs` (preflight always runs; `TryRebuildPluginIndex` checks `IndexType`)
- [x] `LiteDB/Engine/Services/RebuildService.cs`
- [x] `LiteDB/Engine/FileReader/FileReaderV8.cs` (don't swallow `PLUGIN_REQUIRED`; `LoadIndexes` `IndexType` handling)
- [x] `LiteDB/Engine/Pages/CollectionPage.cs` (add a fault-tolerant scan helper for `$plugins`/validation that does not throw on legacy/corrupt metadata and does not compile `BsonExpression`)
- [x] `LiteDB/Engine/LiteEngine.cs` (`SetPluginContext` + validation-on-open)
- [x] (Spatial plugin): fix `SpatialPluginRegistry.Attach` to not capture `LiteDatabase`
