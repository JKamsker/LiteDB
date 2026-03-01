# Internal design & implementation details

## A) Refactor LiteDatabase construction to support builder/factory

File: `LiteDB/Client/Database/LiteDatabase.cs` (modify)

Add an internal constructor that can:

- accept an already-created plugin context
- optionally skip plugin initialization
- optionally release a factory/engine lease on dispose (for factory reuse)

Example signature (exact can vary, but must be decision-complete in behavior):

internal LiteDatabase(
    ILiteEngine engine,
    bool disposeOnClose,
    BsonMapper mapper,
    DefaultPluginContext pluginContext,
    bool initializePlugins,
    IEnumerable<ILitePlugin> plugins,
    ConnectionString connectionStringForContext,
    IDisposable engineLease = null)

Rules:

- Public constructors continue to create `DefaultPluginContext` and call existing initialization path.
- Even when plugin initialization is skipped (`initializePlugins = false`), still apply the `pluginContext` to the engine via `IPluginHost.SetPluginContext(pluginContext)` (mirrors the existing `InitializePlugins(...)` behavior).
- Reuse-engine factory path (FactoryReuse.ReuseEngine):
  - Create one `DefaultPluginContext` + one internal “plugin host” `LiteDatabase` that runs plugin initialization once.
  - Returned `LiteDatabase` handles: `initializePlugins = false`, share the same `pluginContext`, and dispose `engineLease` exactly once when disposed (instead of disposing the engine).

Recommended lease/refcount model (deterministic):

- The factory holds one owning reference while it is not disposed.
- Each `CreateDatabase()` returns a handle with its own `engineLease` reference.
- Disposing the factory prevents new handles and releases the owning reference.
- Disposing the factory must not invalidate existing handles; they remain usable until disposed.
- The reused engine is disposed when the last reference is released (factory disposed + all handles disposed).
- The internal “plugin host” used for initialization must not release a lease (it should not own a reference).
- `CreateDatabase()` must be thread-safe: refcounts use `Interlocked`, and plugin initialization is guarded to run exactly once even under concurrent calls.

Notes:

- Because `ILitePlugin.Initialize` receives a `LiteDatabase` instance, factory reuse relies on a documented constraint that plugin initialization is registration-only and must not capture the passed database instance.
- Engine/plugin context must be treated as a fixed pair in reuse mode (do not swap contexts on a reused engine instance).
- If the reused engine is `SharedEngine`, ensure mutex acquisition/release is reentrancy-safe for nested operations (existing recursion tests cover this usage pattern).
- Ensure handle disposal is idempotent so a double-dispose cannot underflow a factory refcount/lease.
- Ensure the factory/lease is released only from explicit disposal (`Dispose(true)`), never from a finalizer path (`Dispose(false)`).

Also ensure the existing stream constructor’s checkpoint override behavior remains identical:

- If `logStream == null` and stream is not `MemoryStream` and the stream is writable, force checkpoint size to 1 and restore on dispose.

---

## B) Missing-plugin safety semantics (host-controlled)

File: `LiteDB/Engine/Services/SnapShot.cs` (modify)

Current behavior:

- Detect plugin index metadata entries via `CollectionPage.GetPluginIndexes()` (does not catch `IndexType != 0` indexes when the metadata entry is missing/corrupt).
- If `DiagnosticPolicy.MissingBehavior == RefuseDatabase`, throw.
- Else log a warning once per pluginId and allow the snapshot (warn-once cache is process-wide and keyed only by pluginId; `RefuseOperations` / `AllowIfSafe` do not currently refuse writes/DDL).

Update behavior to match “refuse by default; allow read-only if configured”:

- Define “plugin-owned assets” as:
  - any plugin metadata entry (`CollectionPage.GetPluginIndexes()`), and/or
  - any `CollectionIndex` with `IndexType != 0` (even if metadata is missing/corrupt).
- `RefuseDatabase`: throw whenever plugin-owned assets are encountered (read or write snapshots).
- `RefuseOperations`: allow opening the database and unaffected collections, but refuse any snapshot (read or write) on collections that contain plugin-owned assets.
- `AllowIfSafe`:
  - Read snapshot on an affected collection: warn once, allow.
  - Write snapshot (and DDL) touching an affected collection: throw `PLUGIN_REQUIRED` (prevents stale plugin indexes / inconsistency).

Implementation approach:

- In `EvaluatePluginAssets()`, surface both plugin metadata entries and `IndexType != 0` indexes; metadata is used to improve diagnostics, not to decide safety.
- In `HandleMissingPluginAsset(...)`, branch on `LiteDatabaseOptions.MissingPluginBehavior` (enforcement) and `_mode`:
  - `AllowIfSafe` only permits `LockMode.Read` snapshots.
- Do not treat a “loaded pluginId descriptor” as sufficient for write safety: any `IndexType != 0` index requires an installed `IIndexStrategy` for its persisted `IndexType` (`_plugins?.Indexes?.GetByType(index.IndexType)`), especially for write/DDL paths that would need to maintain or drop the index.
- Warning/warn-once caching must not suppress diagnostics across databases: cache by a database identity + pluginId (or `<unknown>:type:{indexType}` when pluginId is unknown). `$plugins` and validation-on-open must not populate this cache.
- When `pluginId` is unknown (for example, `IndexType != 0` but metadata is missing/corrupt), include `indexType` in diagnostics and use a stable cache-key segment (e.g., `"<unknown>:type:{indexType}"`) as part of the db-scoped key.
- Core warning text must be plugin-agnostic (no hard-coded plugin-specific messages); plugin packages/hosts can provide plugin-specific guidance via diagnostic policies.
- This keeps safety guarantees: you never mutate a collection that has plugin-owned index assets when the owning plugin isn’t loaded, unless you explicitly drop those indexes (see rebuild option).
- Note: today `SnapShot.DropCollection` can skip custom-index cleanup when the owning strategy is unavailable; this is why `RefuseOperations`/`AllowIfSafe` must refuse `LockMode.Write` snapshots (and destructive DDL) on affected collections before any partial delete work begins.

---

## C) Prevent core planner from treating plugin indexes as btree

File: `LiteDB/Engine/Query/QueryOptimization.cs` (modify)

Update `ChooseIndex(...)` to consider only `CollectionIndex` entries with `IndexType == 0` (btree). Plugin indexes are only selectable via plugin planning rules (e.g., vector’s `VectorIndexPlanningRule`).

This should be unconditional: core btree planning must never treat plugin indexes as btree.

Ensure the `IndexType == 0` filter applies to both predicate matching and fallback selection paths (including `OrderBy`/`GroupBy` cases that can trigger preferred-index fallbacks).

---

## D) Opt-in “validate plugins on open”

Goal: if enabled, fail fast instead of waiting for first collection access.

Implementation:

- Store requested validation on the plugin context (so it works with SharedEngine recreating LiteEngine instances).
- Add an internal interface (example):
  - File: `LiteDB/Plugins/IPluginValidationState.cs` (new, internal)
  - `bool ValidatePluginAssetsOnOpen { get; set; }`
  - `bool PluginAssetsValidated { get; set; }`
- `DefaultPluginContext` implements it and initializes to false.

Where validation executes:

- File: `LiteDB/Engine/LiteEngine.cs` (modify `IPluginHost.SetPluginContext`)
  - After setting `_plugins`, if:
    - context implements `IPluginValidationState` and `ValidatePluginAssetsOnOpen == true` and `PluginAssetsValidated == false`
  - then run a *non-enforcing collection-page scan* (do not open per-collection snapshots by name):
    - iterate `_header.GetCollections()` in an `AutoTransaction(...)`
    - read each collection page by `pageId` from a neutral snapshot (`"$"`) / direct page access
    - detect affected collections using:
      - `CollectionIndex.IndexType != 0` (authoritative), and/or
      - plugin index metadata entries when readable (diagnostics only)
    - record required pluginIds + affected collections + parse errors into the context for later diagnostics (and `$plugins`)
  - apply host policy to the scan result:
    - `RefuseDatabase`: throw (fail fast) if any affected collection is found where the required plugin is not loaded (or pluginId is unknown / metadata is unparsable)
    - `RefuseOperations`: do not throw; validation only precomputes/report requirements
    - `AllowIfSafe`: do not throw; log/warn (scoped to db identity) but do not populate the enforcement warn-once cache
  - set `PluginAssetsValidated = true` on the context.

Notes:

- Validation-on-open validates plugin-owned index requirements only; it does not attempt to prove that all documents/pages are readable without plugin-defined BSON/page support.
- `PluginAssetsValidated` caching is valid because plugin contexts are scoped to a single engine/database identity; if a future API ever reuses one context across multiple database identities, the cache must be keyed by identity (or reset).
- Treat collection-page parse failures as “affected”: under `RefuseDatabase`, fail fast with aggregated diagnostics; under other modes, record the error and continue scanning.
- Any validation warnings must use the same “database identity” scoping defined in `Intention.md` (file-backed canonical absolute path; otherwise per-engine instance identity).
- This keeps validation opt-in and ensures `SharedEngine` doesn’t rescan on every underlying engine open (because the context survives and remembers it validated).
- Do not rely on a newly introduced persisted header marker being present: legacy databases may already contain plugin-owned assets.
- Under `SharedEngine`, validation runs when the underlying `LiteEngine` is (re)created and `SetPluginContext` executes (it is not guaranteed to run at factory build time, so “fail fast” may occur on first operation).
- `PluginAssetsValidated` is a performance cache only and is not cross-process authoritative under `ConnectionType.Shared`.
- Exception safety requirement: callers (notably `SharedEngine`) must treat exceptions from `IPluginHost.SetPluginContext` (including validation-on-open failures) as open failures: dispose/reset the engine instance and release any mutex/state so the next operation cannot deadlock or leak the shared mutex.

---

## E) $plugins system collection

Files:

- `LiteDB/Engine/SystemCollections/SysPlugins.cs` (new) OR add `SysPlugins()` method in `LiteEngine` partial
- `LiteDB/Engine/SystemCollections/Register.cs` (modify)

Output: one row per `pluginId` (summary), aggregated across user collections (stable schema).

{
  "pluginId": "LiteDB.Vector",
  "collections": ["vectors", "docs"],
  "indexCount": 2,
  "loaded": true,
  "errors": []
}

For `pluginId = "<unknown>"` rows, include `indexTypeCounts` (e.g., `[{ "indexType": 7, "count": 2 }]`) so callers can see which non-btree index types were detected when a pluginId is not recoverable. `indexCount` is the total count for that row (sum of counts when `indexTypeCounts` is present).

Key requirements:

- Must work even when missing-plugin behavior is strict (so users can discover what’s required).
- Must avoid opening per-collection snapshots that would trigger missing-plugin enforcement or populate missing-plugin warning caches.

Implementation approach:

- Open a single read snapshot against `"$"` (so `CollectionPage` is null and no plugin-asset evaluation runs). Do not open per-collection snapshots by collection name.
- Scan collections from `_header.GetCollections()` and read each collection page by `pageId` via the neutral snapshot.
- Detect plugin-owned indexes from:
  - any `CollectionIndex` with `IndexType != 0` (authoritative), and
  - plugin metadata entries (`collectionPage.GetPluginIndexes()`) when readable (diagnostics only).
- Buffer/memory hygiene: copy any required data out of page buffers before clearing, and call `snapshot.Clear()` (or equivalent safepoint) per collection to avoid unbounded page-buffer growth while scanning many collections.
- Aggregate rows by `pluginId` (and use `pluginId = "<unknown>"` when pluginId is not recoverable). Keep schema stable: do not emit ad-hoc “error rows” with a different shape.
- For collection-page parse failures (legacy/corrupt metadata), record errors as data (e.g., `{ collection, pageId, errorType, errorMessage, exceptionCode? }`) and continue scanning. Ensure the `<unknown>` row still includes the affected collection name even if `indexCount` is partial/incomplete.
- Compute `loaded` by checking `_plugins?.CustomIndexes?.Registered` contains a descriptor with matching `PluginId`.

Registration:

- Add `this.RegisterSystemCollection("$plugins", () => this.SysPlugins());`

---

## F) Rebuild: opt-in dropping orphaned plugin indexes

Files:

- `LiteDB/Engine/Structures/RebuildOptions.cs` (modify)
- `LiteDB/Engine/Engine/Rebuild.cs` (modify)
- `LiteDB/Engine/Services/RebuildService.cs` (modify)
- `LiteDB/Engine/FileReader/FileReaderV8.cs` (modify)

### Behavior

- Default: unchanged. Missing plugin metadata/strategy during rebuild throws `PLUGIN_REQUIRED`.
- If `DropOrphanedPluginIndexes == true`:
  - Rebuild proceeds even if plugin-owned indexes cannot be recreated.
  - Those indexes are omitted from the rebuilt database.
  - Emit warnings to `ILogger` when available and record dropped indexes into the rebuild report (`_rebuild_errors`) so there is a durable audit trail even when no plugin context/logger is present (include a structured reason code, not only exception text).
  - This is **index salvage only**: rebuild may drop plugin-owned index storage (including plugin page types used only for those indexes), but must still fail by default if any **user documents** or **required core pages** cannot be decoded due to missing plugin-defined BSON types/page factories (no implicit “best-effort” document skipping). A separate explicit “allow data loss / skip unreadable documents” option would be required for that (out of scope here).
  - Require an audit trail: when `DropOrphanedPluginIndexes == true`, ensure the rebuild report is enabled and persisted even when there are no other read errors (do not allow a salvage rebuild without durable reporting; if `IncludeErrorReport == false`, either force it on or reject the option combination).

### Required plumbing

1. `LiteEngine.Rebuild(RebuildOptions options)` passes options through to `RebuildService.Rebuild(...)`.
   - Because rebuild is DDL, perform a rebuild-specific affected-collection preflight *before* any `Close()`/file-replace work that fails fast when plugin support is missing (based on `CollectionIndex.IndexType != 0`, not only plugin metadata entries), independent of `PluginMissingBehavior`. Only bypass this preflight when `DropOrphanedPluginIndexes == true`.
2. `RebuildService` passes `options.DropOrphanedPluginIndexes` into `FileReaderV8` (new ctor param, e.g. `allowOrphanedPluginIndexes`).
3. `FileReaderV8.LoadIndexes()`:
   - Must always capture `pluginId` + raw metadata bytes from `collectionPage.GetPluginIndexes()` even when metadata registry is missing.
   - Treat any index with `IndexType != 0` as plugin-owned even if metadata cannot be parsed (pluginId may be unknown); it must never be rebuilt as a btree index.
   - When `allowOrphanedPluginIndexes == true`, do not throw `CreateMetadataSerializerException`.
   - Populate `IndexInfo.PluginId` + `IndexInfo.PluginMetadata` from the raw plugin index map even when no descriptor exists (this prevents accidentally rebuilding a plugin index as a btree index).
4. `LiteEngine.RebuildContent(...)` must accept `RebuildOptions` so it can decide what to do per index (and prevent fall-through to `EnsureIndex(...)` for plugin-owned indexes).
5. `TryRebuildPluginIndex(collection, index, options)`:
   - Define “plugin-owned index” as “has plugin metadata and pluginId” OR “`IndexType != 0`”.
   - Define “orphaned” as “plugin-owned index that cannot be rebuilt (plugin missing, missing registries/descriptor/strategy kind, metadata deserialization failure, etc.)”.
   - If plugin-owned index detected and it cannot be rebuilt:
     - if `DropOrphanedPluginIndexes == true`: record a warning/report entry (for example, into `_rebuild_errors`) and return true (meaning “handled; skip”).
     - else: throw as today.
6. `FileReaderV8.GetDocuments()` (or its caller) must treat missing-plugin decode as **fatal by default** and abort rebuild (for example: unknown plugin BSON type codes, missing page factories required to decode user documents, or other plugin-required errors). Do not silently skip documents unless a future explicit “allow data loss” option is introduced. Non-plugin corruption errors may still be recorded into the rebuild report per current behavior.

### Safety defaults

- `Recovery()` (auto rebuild on invalid state) does not enable dropping indexes and does not allow document skipping; remains strict.
- Ensure rebuild failure does not leave the `LiteEngine` instance permanently closed/disposed (use `try/finally` around close/reopen/rename operations).
