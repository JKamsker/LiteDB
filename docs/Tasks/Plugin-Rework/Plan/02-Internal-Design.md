# Internal design & implementation details

## A) Refactor LiteDatabase construction to support builder/factory

File: `LiteDB/Client/Database/LiteDatabase.cs` (modify)

Add an internal constructor that can:

- accept an already-created plugin context
- optionally skip plugin initialization
- optionally notify an owning factory on dispose

Example signature (exact can vary, but must be decision-complete in behavior):

internal LiteDatabase(
    ILiteEngine engine,
    bool disposeOnClose,
    BsonMapper mapper,
    DefaultPluginContext pluginContext,
    bool initializePlugins,
    IEnumerable<ILitePlugin> plugins,
    ConnectionString connectionStringForContext,
    LiteDatabaseFactory owningFactory = null)

Rules:

- Public constructors continue to create `DefaultPluginContext` and call existing initialization path.
- Shared-engine factory path:
  - Create one `DefaultPluginContext` + one internal “plugin host” `LiteDatabase` that runs plugin initialization once.
  - Returned client wrappers: `initializePlugins = false`, share the same `pluginContext`, and call `owningFactory.OnDatabaseDisposed()` when disposed (instead of disposing the engine).

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

- `RefuseDatabase`: unchanged (throw during snapshot creation for collections with plugin assets).
- `AllowIfSafe` (and `RefuseOperations` for now):
  - Read snapshot: warn once, allow.
  - Write snapshot: throw `PLUGIN_REQUIRED` (prevents stale plugin indexes / inconsistency).

Implementation approach:

- In `HandleMissingPluginAsset(...)`, branch on `_mode`:
  - If `_mode == LockMode.Write` and behavior != `RefuseDatabase`, still throw via policy.
- Keep warning cache behavior as-is (once per `pluginId`).
- This keeps safety guarantees: you never mutate a collection that has plugin-owned index assets when the owning plugin isn’t loaded, unless you explicitly drop those indexes (see rebuild option).

---

## C) Prevent core planner from treating plugin indexes as btree

File: `LiteDB/Engine/Query/QueryOptimization.cs` (modify)

Update `ChooseIndex(...)` to consider only `CollectionIndex` entries with `IndexType == 0` (btree). Plugin indexes are only selectable via plugin planning rules (e.g., vector’s `VectorIndexPlanningRule`).

This is required for “AllowIfSafe” read scenarios and avoids accidental misuse of plugin indexes by btree logic.

---

## D) Persisted header marker: HAS_PLUGIN_INDEXES

Files:

- `LiteDB/Engine/EnginePragmas.cs` (modify)
- `LiteDB/Engine/Pragmas.cs` (modify)

Add a persisted pragma in the reserved pragma area:

- Offset: 109 (1 byte)
- Name constant: `Pragmas.HAS_PLUGIN_INDEXES` (string)

EnginePragmas additions:

- `public const int P_HAS_PLUGIN_INDEXES = 109;`
- `public bool HasPluginIndexes { get; private set; }`
- Add pragma entry:
  - Get returns boolean
  - Read reads byte/bool
  - Write writes bool
  - Validate should throw on user-set attempts (read-only) or ignore; choose one and document it.

Setting the marker (sticky):

- File: `LiteDB/Engine/Engine/Index.cs` (modify)
- In `EnsureCustomIndex(...)` (or after successful plugin index creation), set:
  - `_header.Pragmas.Set(Pragmas.HAS_PLUGIN_INDEXES, true, validate:false)` (or an internal helper)
- Sticky: never cleared automatically.

---

## E) Opt-in “validate plugins on open”

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
    - and `_header.Pragmas.HasPluginIndexes == true`
  - then run a scan similar to current rebuild pre-scan:
    - iterate `_header.GetCollections()` in an `AutoTransaction(...)`
    - open read snapshots for each collection
    - rely on Snapshot’s missing-plugin behavior to throw/warn
  - set `PluginAssetsValidated = true` on the context.

Notes:

- This keeps validation opt-in and ensures SharedEngine doesn’t rescan on every underlying engine open (because the context survives and remembers it validated).
- Validation only runs when header marker is set (fast path).

---

## F) $plugins system collection

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
- Therefore, it must not reuse cached snapshots that bypass validation.

Implementation approach:

- In `SysPlugins`, create uncached, read-only snapshots with plugin-validation disabled:
  - Add a new internal method on `TransactionService` such as:
    - `Snapshot CreateUncachedSnapshot(LockMode mode, string collection, bool addIfNotExists, bool validatePluginAssets)`
  - Use `validatePluginAssets: false` to prevent throwing.
  - Dispose each snapshot immediately.
- For each snapshot’s `CollectionPage`, call `GetPluginIndexes()` (raw) and aggregate by `pluginId`.
- Compute loaded by checking `_plugins?.CustomIndexes?.Registered` contains a descriptor with matching `PluginId`.

Registration:

- Add `this.RegisterSystemCollection("$plugins", () => this.SysPlugins());`

---

## G) Rebuild: opt-in dropping orphaned plugin indexes

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
  - Emit warnings to `ILogger` (plugin context logger) and/or include an entry in rebuild error report.

### Required plumbing

1. `LiteEngine.Rebuild(RebuildOptions options)` passes options through to `RebuildService.Rebuild(...)`.
2. `RebuildService` passes `options.DropOrphanedPluginIndexes` into `FileReaderV8` (new ctor param, e.g. `allowOrphanedPluginIndexes`).
3. `FileReaderV8.LoadIndexes()`:
   - Must always capture `pluginId` + raw metadata bytes from `collectionPage.GetPluginIndexes()` even when metadata registry is missing.
   - When `allowOrphanedPluginIndexes == true`, do not throw `CreateMetadataSerializerException`.
   - Populate `IndexInfo.PluginId` + `IndexInfo.PluginMetadata` from raw plugin index map even when no descriptor exists.
4. `LiteEngine.RebuildContent(...)` must accept `RebuildOptions` so it can decide what to do per index.
5. `TryRebuildPluginIndex(collection, index, options)`:
   - If plugin index detected and plugin cannot rebuild it (missing registry/descriptor/strategy kind):
     - if `DropOrphanedPluginIndexes == true`: log warning and return true (meaning “handled; skip”).
     - else: throw as today.

### Safety defaults

- `Recovery()` (auto rebuild on invalid state) does not enable dropping indexes; remains strict.
