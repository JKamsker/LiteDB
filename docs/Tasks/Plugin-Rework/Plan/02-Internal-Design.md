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
- Reuse-engine factory path (FactoryReuse.ReuseEngine):
  - Create one `DefaultPluginContext` + one internal “plugin host” `LiteDatabase` that runs plugin initialization once.
  - Returned `LiteDatabase` handles: `initializePlugins = false`, share the same `pluginContext`, and dispose `engineLease` exactly once when disposed (instead of disposing the engine).

Recommended lease/refcount model (deterministic):

- The factory holds one owning reference while it is not disposed.
- Each `CreateDatabase()` returns a handle with its own `engineLease` reference.
- Disposing the factory prevents new handles and releases the owning reference.
- Disposing the factory must not invalidate existing handles; they remain usable until disposed.
- The shared engine is disposed when the last reference is released (factory disposed + all handles disposed).
- The internal “plugin host” used for initialization must not release a lease (it should not own a reference).

Notes:

- Because `ILitePlugin.Initialize` receives a `LiteDatabase` instance, factory reuse relies on a documented constraint that plugin initialization is registration-only and must not capture the passed database instance.
- Engine/plugin context must be treated as a fixed pair in reuse mode (do not swap contexts on a shared engine instance).
- If the reused engine is `SharedEngine`, ensure mutex acquisition/release is reentrancy-safe for nested operations (existing recursion tests cover this usage pattern).
- Ensure handle disposal is idempotent so a double-dispose cannot underflow a factory refcount/lease.

Also ensure the existing stream constructor’s checkpoint override behavior remains identical:

- If `logStream == null` and stream is not `MemoryStream` and the stream is writable, force checkpoint size to 1 and restore on dispose.

---

## B) Missing-plugin safety semantics (host-controlled)

File: `LiteDB/Engine/Services/SnapShot.cs` (modify)

Current behavior:

- Detect plugin-owned indexes in the collection page.
- If `MissingBehavior == RefuseDatabase`, throw.
- Else warn once per plugin.

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
- In `HandleMissingPluginAsset(...)`, branch on `MissingBehavior` and `_mode`:
  - `AllowIfSafe` only permits `LockMode.Read` snapshots.
- Keep warning cache behavior as-is (once per `pluginId`).
- When `pluginId` is unknown (for example, `IndexType != 0` but metadata is missing/corrupt), include `indexType` in diagnostics and use a stable warning-cache key (e.g., `"<unknown>:type:{indexType}"`) to avoid null/ambiguous cache entries.
- This keeps safety guarantees: you never mutate a collection that has plugin-owned index assets when the owning plugin isn’t loaded, unless you explicitly drop those indexes (see rebuild option).

---

## C) Prevent core planner from treating plugin indexes as btree

File: `LiteDB/Engine/Query/QueryOptimization.cs` (modify)

Update `ChooseIndex(...)` to consider only `CollectionIndex` entries with `IndexType == 0` (btree). Plugin indexes are only selectable via plugin planning rules (e.g., vector’s `VectorIndexPlanningRule`).

This should be unconditional: core btree planning must never treat plugin indexes as btree.

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
  - then run a scan similar to current rebuild pre-scan:
    - iterate `_header.GetCollections()` in an `AutoTransaction(...)`
    - open read snapshots (`addIfNotExists: false`) for each collection
    - rely on Snapshot’s missing-plugin behavior to throw/warn
  - set `PluginAssetsValidated = true` on the context.

Notes:

- This keeps validation opt-in and ensures `SharedEngine` doesn’t rescan on every underlying engine open (because the context survives and remembers it validated).
- Do not rely on a newly introduced persisted header marker being present: legacy databases may already contain plugin-owned assets.
- Under `SharedEngine`, validation runs when the underlying `LiteEngine` is (re)created and `SetPluginContext` executes (it is not guaranteed to run at factory build time).
- `PluginAssetsValidated` is a performance cache only and is not cross-process authoritative under `ConnectionType.Shared`.

---

## E) $plugins system collection

Files:

- `LiteDB/Engine/SystemCollections/SysPlugins.cs` (new) OR add `SysPlugins()` method in `LiteEngine` partial
- `LiteDB/Engine/SystemCollections/Register.cs` (modify)

Output per `pluginId`, aggregated across user collections:

{
  "pluginId": "LiteDB.Vector",
  "collections": ["vectors", "docs"],
  "indexCount": 2,
  "loaded": true
}

Key requirements:

- Must work even when missing-plugin behavior is strict (so users can discover what’s required).
- Must avoid opening per-collection snapshots that would trigger missing-plugin enforcement or populate missing-plugin warning caches.

Implementation approach:

- In `SysPlugins`, open a single read snapshot against `"$"` (so `CollectionPage` is null and no plugin-asset evaluation runs).
- For each user collection returned by `_header.GetCollections()`:
  - Attempt to read the `CollectionPage` via `Snapshot.GetPage<CollectionPage>(pageId)`.
  - Catch `InvalidCastException` / `LiteException` / unexpected exceptions and emit a best-effort row instead of throwing. Suggested error row fields:
    - `collection`, `pageId`, `errorType`, `errorMessage` (and optionally `exceptionCode` when available).
  - Detect plugin-owned assets from:
    - plugin metadata entries (`collectionPage.GetPluginIndexes()`), and
    - any `CollectionIndex` with `IndexType != 0` (even if metadata is missing/corrupt).
    - Note: `IndexType` detection requires the `CollectionPage` to be parsable; if parsing fails (legacy/corrupt metadata throws during `CollectionPage` construction), `$plugins` can only emit an error row for that collection unless we add a dedicated “indexes-only” parser.
  - For `IndexType != 0` without metadata, report `pluginId = "<unknown>"` and include the `indexType`.
  - Copy any required data out of the `CollectionPage` before calling `snapshot.Clear()` (because `Clear()` releases page buffers).
  - Call `snapshot.Clear()` after each collection to release page buffers.
- Compute loaded by checking `_plugins?.CustomIndexes?.Registered` contains a descriptor with matching `PluginId`.

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
  - Emit warnings to `ILogger` when available and record dropped indexes into the existing rebuild report (`_rebuild_errors`) so there is a durable audit trail even when no plugin context/logger is present.

### Required plumbing

1. `LiteEngine.Rebuild(RebuildOptions options)` passes options through to `RebuildService.Rebuild(...)`.
   - When `DropOrphanedPluginIndexes == true`, skip the strict pre-scan (`EnsurePluginAssetsAllowed`) that would otherwise throw when the plugin is missing.
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

### Safety defaults

- `Recovery()` (auto rebuild on invalid state) does not enable dropping indexes; remains strict.
