Proposed Plan


  # LiteDB Plugin System: Builder + Factory + Safety/Introspection

  ## Summary

  Introduce a fluent LiteDatabaseBuilder and an ILiteDatabaseFactory that can either (a) create fresh databases from captured configuration, or (b) share a single in-process engine/context across multiple
  CreateDatabase() calls. Strengthen safety by:

  - Making missing-plugin behavior host-controlled (default stays strict).
  - Persisting a header marker (HAS_PLUGIN_INDEXES) when plugin-owned indexes exist.
  - Adding an opt-in “validate plugin assets on open” scan.
  - Adding a $plugins system collection for plugin requirement introspection.
  - Adding an explicit rebuild opt-in to drop orphaned plugin indexes when plugins are missing.

  This is additive: keep all existing LiteDatabase constructors working and behavior-compatible by default.

  ———

  ## Goals / Non-goals

  ### Goals

  - Fluent, composable initialization (UsePlugin, UseFile, UseInMemory, …).
  - BuildFactory() that supports:
      - Config-only mode (new engine + new plugin context per database)
      - Shared-engine mode (one engine + one plugin context shared; ref-counted)
  - Safety-first plugin handling:
      - Default remains RefuseDatabase (current behavior).
      - Optional “read-only safe access” mode: allow reads but prevent writes when plugin assets are present and the plugin is missing.
  - Explicit, opt-in rebuild behavior to prevent accidental index loss.
  - Introspection: $plugins system collection.

  ### Non-goals (for this iteration)

  - Persisting a full per-plugin manifest (versions, per-asset criticality, etc.).
  - Making plugin-defined BSON types readable without the plugin (compat shims).
  - General-purpose plugin page scanning/introspection beyond what can be derived from collection metadata/indexes.

  ———

  ## Public API changes (additive)

  ### 1) LiteDatabaseBuilder (new)

  File: LiteDB/Client/Database/LiteDatabaseBuilder.cs
  Namespace: LiteDB

  Proposed surface (fluent, returns this):

  Plugin registration (store factories internally):

  - UsePlugin(ILitePlugin plugin)
  - UsePlugin<TPlugin>() where TPlugin : ILitePlugin, new()
  - UsePlugin(Func<ILitePlugin> pluginFactory)
  - UsePlugins(IEnumerable<ILitePlugin> plugins)

  Data source (mutually exclusive, last call wins):

  - UseFile(string filename) (shorthand for UseConnectionString("Filename=..."))
  - UseConnectionString(string connectionString)
  - UseConnectionString(ConnectionString connectionString)
  - UseInMemory() (shorthand for UseConnectionString(":memory:"))
  - UseStream(Stream dataStream, Stream logStream = null) (mirrors existing ctor semantics)
  - UseEngine(ILiteEngine engine, bool disposeOnFactoryDispose = true)
      - Note: when UseEngine(...) is chosen, BuildFactory() must use shared-engine mode (see below).

  Configuration:

  - WithMapper(BsonMapper mapper)
  - WithServices(IServiceProvider services)
  - WithLogger(ILogger logger)
  - WithPassword(string password) (applies to connection-string/engine settings where relevant)
  - AsReadOnly() (connection string ReadOnly / engine settings)
  - WithConnectionType(ConnectionType type) (Direct vs ConnectionType.Shared mutex mode)
  - ConfigureEngine(Action<EngineSettings> configure) (passed into ConnectionString.CreateEngine)
  - WithMissingPluginBehavior(PluginMissingBehavior behavior) (host-controlled, see below)
  - ValidatePluginsOnOpen(bool enabled = true) (opt-in global scan)
  - WithEngineReuse(EngineReuse reuse) (for BuildFactory(), see below)

  Build:

  - ILiteDatabase Build()
  - ILiteDatabaseFactory BuildFactory()

  ### 2) Engine reuse toggle for factories (new)

  File: LiteDB/Client/Database/EngineReuse.cs
  Namespace: LiteDB

  public enum EngineReuse
  {
      None = 0,   // config-only factory
      Shared = 1  // shared-engine factory (in-process reuse)
  }

  ### 3) ILiteDatabaseFactory + LiteDatabaseFactory (new)

  Files:

  - LiteDB/Client/Database/ILiteDatabaseFactory.cs
  - LiteDB/Client/Database/LiteDatabaseFactory.cs

  Interface:

  public interface ILiteDatabaseFactory : IDisposable
  {
      ILiteDatabase CreateDatabase();
  }

  Behavior modes (driven by builder’s WithEngineReuse(...)):

  - EngineReuse.None: each CreateDatabase() creates:
      - new engine (from connection string/stream settings),
      - new DefaultPluginContext,
      - initializes plugins once for that database.
  - EngineReuse.Shared: factory owns:
      - one engine instance,
      - one plugin context instance,
      - one “plugin host” LiteDatabase instance used only for plugin initialization (kept alive for factory lifetime),
      - ref-counted “client” LiteDatabase wrappers returned from CreateDatabase() that share engine/context and do not initialize plugins.

  ### 4) Host-controlled missing plugin behavior

  File: LiteDB/Client/Database/LiteDatabaseOptions.cs (modify)

  Add:

  - public PluginMissingBehavior MissingPluginBehavior { get; set; } = PluginMissingBehavior.RefuseDatabase;
  - public bool ValidatePluginsOnOpen { get; set; } = false; (optional; builder sets this too)

  Builder writes these options regardless of whether user uses the builder or constructors. Existing constructors + LiteDatabaseOptions should honor the new fields.

  ### 5) Rebuild option: drop orphaned plugin indexes (new)

  File: LiteDB/Engine/Structures/RebuildOptions.cs (modify)

  Add:

  public bool DropOrphanedPluginIndexes { get; set; } = false;

  ———

  ## Internal design & implementation details

  ## A) Refactor LiteDatabase construction to support builder/factory

  File: LiteDB/Client/Database/LiteDatabase.cs (modify)

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

  - Public constructors continue to create DefaultPluginContext and call existing initialization path.
  - Shared-engine factory path:
      - Create one DefaultPluginContext + one internal “plugin host” LiteDatabase that runs plugin initialization once.
      - Returned client wrappers: initializePlugins = false, share the same pluginContext, and call owningFactory.OnDatabaseDisposed() when disposed (instead of disposing the engine).

  Also ensure the existing stream constructor’s checkpoint override behavior remains identical:

  - If logStream == null and stream is not MemoryStream and the stream is writable, force checkpoint size to 1 and restore on dispose.

  ———

  ## B) Missing-plugin safety semantics (host-controlled)

  File: LiteDB/Engine/Services/SnapShot.cs (modify)

  Current behavior:

  - Detect plugin-owned indexes in the collection page.
  - If MissingBehavior == RefuseDatabase, throw.
  - Else warn once per plugin.

  Update behavior to match “refuse by default; allow read-only if configured”:

  - RefuseDatabase: unchanged (throw during snapshot creation for collections with plugin assets).
  - AllowIfSafe (and RefuseOperations for now):
      - Read snapshot: warn once, allow.
      - Write snapshot: throw PLUGIN_REQUIRED (prevents stale plugin indexes / inconsistency).

  Implementation approach:

  - In HandleMissingPluginAsset(...), branch on _mode:
      - If _mode == LockMode.Write and behavior != RefuseDatabase, still throw via policy.
  - Keep warning cache behavior as-is (once per pluginId).
  - This keeps safety guarantees: you never mutate a collection that has plugin-owned index assets when the owning plugin isn’t loaded, unless you explicitly drop those indexes (see rebuild option).

  ———

  ## C) Prevent core planner from treating plugin indexes as btree

  File: LiteDB/Engine/Query/QueryOptimization.cs (modify)

  Update ChooseIndex(...) to consider only CollectionIndex entries with IndexType == 0 (btree). Plugin indexes are only selectable via plugin planning rules (e.g., vector’s VectorIndexPlanningRule).

  This is required for “AllowIfSafe” read scenarios and avoids accidental misuse of plugin indexes by btree logic.

  ———

  ## D) Persisted header marker: HAS_PLUGIN_INDEXES

  Files:

  - LiteDB/Engine/EnginePragmas.cs (modify)
  - LiteDB/Engine/Pragmas.cs (modify)

  Add a persisted pragma in the reserved pragma area:

  - Offset: 109 (1 byte)
  - Name constant: Pragmas.HAS_PLUGIN_INDEXES (string)

  EnginePragmas additions:

  - public const int P_HAS_PLUGIN_INDEXES = 109;
  - public bool HasPluginIndexes { get; private set; }
  - Add pragma entry:
      - Get returns boolean
      - Read reads byte/bool
      - Write writes bool
      - Validate should throw on user-set attempts (read-only) or ignore; choose one and document it.

  Setting the marker (sticky):

  - File: LiteDB/Engine/Engine/Index.cs (modify)
  - In EnsureCustomIndex(...) (or after successful plugin index creation), set:
      - _header.Pragmas.Set(Pragmas.HAS_PLUGIN_INDEXES, true, validate:false) (or an internal helper)
  - Sticky: never cleared automatically.

  ———

  ## E) Opt-in “validate plugins on open”

  Goal: if enabled, fail fast instead of waiting for first collection access.

  Implementation:

  - Store requested validation on the plugin context (so it works with SharedEngine recreating LiteEngine instances).
  - Add an internal interface (example):
      - File: LiteDB/Plugins/IPluginValidationState.cs (new, internal)
      - bool ValidatePluginAssetsOnOpen { get; set; }
      - bool PluginAssetsValidated { get; set; }
  - DefaultPluginContext implements it and initializes to false.

  Where validation executes:

  - File: LiteDB/Engine/LiteEngine.cs (modify IPluginHost.SetPluginContext)
      - After setting _plugins, if:
          - context implements IPluginValidationState and ValidatePluginAssetsOnOpen == true and PluginAssetsValidated == false
          - and _header.Pragmas.HasPluginIndexes == true
      - then run a scan similar to current rebuild pre-scan:
          - iterate _header.GetCollections() in an AutoTransaction(...)
          - open read snapshots for each collection
          - rely on Snapshot’s missing-plugin behavior to throw/warn
      - set PluginAssetsValidated = true on the context.

  Notes:

  - This keeps validation opt-in and ensures SharedEngine doesn’t rescan on every underlying engine open (because the context survives and remembers it validated).
  - Validation only runs when header marker is set (fast path).

  ———

  ## F) $plugins system collection

  Files:

  - LiteDB/Engine/SystemCollections/SysPlugins.cs (new) OR add SysPlugins() method in LiteEngine partial
  - LiteDB/Engine/SystemCollections/Register.cs (modify)

  Output per pluginId, aggregated across user collections:

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

  - In SysPlugins, create uncached, read-only snapshots with plugin-validation disabled:
      - Add a new internal method on TransactionService such as:
          - Snapshot CreateUncachedSnapshot(LockMode mode, string collection, bool addIfNotExists, bool validatePluginAssets)
      - Use validatePluginAssets: false to prevent throwing.
      - Dispose each snapshot immediately.
  - For each snapshot’s CollectionPage, call GetPluginIndexes() (raw) and aggregate by pluginId.
  - Compute loaded by checking _plugins?.CustomIndexes?.Registered contains a descriptor with matching PluginId.

  Registration:

  - Add this.RegisterSystemCollection(\"$plugins\", () => this.SysPlugins());

  ———

  ## G) Rebuild: opt-in dropping orphaned plugin indexes

  Files:

  - LiteDB/Engine/Structures/RebuildOptions.cs (modify)
  - LiteDB/Engine/Engine/Rebuild.cs (modify)
  - LiteDB/Engine/Services/RebuildService.cs (modify)
  - LiteDB/Engine/FileReader/FileReaderV8.cs (modify)

  ### Behavior

  - Default: unchanged. Missing plugin metadata/strategy during rebuild throws PLUGIN_REQUIRED.
  - If DropOrphanedPluginIndexes == true:
      - Rebuild proceeds even if plugin-owned indexes cannot be recreated.
      - Those indexes are omitted from the rebuilt database.
      - Emit warnings to ILogger (plugin context logger) and/or include an entry in rebuild error report.

  ### Required plumbing

  1. LiteEngine.Rebuild(RebuildOptions options) passes options through to RebuildService.Rebuild(...).
  2. RebuildService passes options.DropOrphanedPluginIndexes into FileReaderV8 (new ctor param, e.g. allowOrphanedPluginIndexes).
  3. FileReaderV8.LoadIndexes():
      - Must always capture pluginId + raw metadata bytes from collectionPage.GetPluginIndexes() even when metadata registry is missing.
      - When allowOrphanedPluginIndexes == true, do not throw CreateMetadataSerializerException.
      - Populate IndexInfo.PluginId + IndexInfo.PluginMetadata from raw plugin index map even when no descriptor exists.
  4. LiteEngine.RebuildContent(...) must accept RebuildOptions so it can decide what to do per index.
  5. TryRebuildPluginIndex(collection, index, options):
      - If plugin index detected and plugin cannot rebuild it (missing registry/descriptor/strategy kind):
          - if DropOrphanedPluginIndexes == true: log warning and return true (meaning “handled; skip”).
          - else: throw as today.

  ### Safety defaults

  - Recovery() (auto rebuild on invalid state) does not enable dropping indexes; remains strict.

  ———

  ## Files summary (expected touch points)

  New

  - LiteDB/Client/Database/LiteDatabaseBuilder.cs
  - LiteDB/Client/Database/EngineReuse.cs
  - LiteDB/Client/Database/ILiteDatabaseFactory.cs
  - LiteDB/Client/Database/LiteDatabaseFactory.cs
  - LiteDB/Engine/SystemCollections/SysPlugins.cs (or equivalent partial method)
  - LiteDB/Plugins/IPluginValidationState.cs (internal helper interface)

  Modify

  - LiteDB/Client/Database/LiteDatabase.cs
  - LiteDB/Client/Database/LiteDatabaseOptions.cs
  - LiteDB/Plugins/DefaultPluginContext.cs (store validation flags; host policy application)
  - LiteDB/Engine/Services/SnapShot.cs (write-mode refusal under AllowIfSafe/RefuseOperations)
  - LiteDB/Engine/Query/QueryOptimization.cs (ignore non-btree indexes)
  - LiteDB/Engine/EnginePragmas.cs + LiteDB/Engine/Pragmas.cs (HAS_PLUGIN_INDEXES)
  - LiteDB/Engine/Engine/Index.cs (set header marker for custom indexes)
  - LiteDB/Engine/SystemCollections/Register.cs (register $plugins)
  - LiteDB/Engine/Structures/RebuildOptions.cs
  - LiteDB/Engine/Engine/Rebuild.cs
  - LiteDB/Engine/Services/RebuildService.cs
  - LiteDB/Engine/FileReader/FileReaderV8.cs
  (Optionally) update plugin docs to state missing-plugin policy is host-controlled.
  ———

  ## Test plan (new/updated coverage)

  ### Builder / Factory

  1. Builder smoke: build :memory: db with vector plugin; create vector index; query.
  2. Factory (EngineReuse.None):
      - Create factory for :memory:; create db1/db2; assert isolated contexts (e.g., plugin registrations don’t leak if you use per-build plugin factories).
  3. Factory (EngineReuse.Shared):
      - Create factory with shared engine; CreateDatabase() twice.
      - Verify plugin initialization ran once (use a tracking plugin).
      - Verify disposing db wrappers does not dispose engine until factory disposed (or until factory disposed + refcount==0, per implementation).
  4. Dispose ordering:
      - Dispose factory first, then dispose db wrappers; ensure no crashes/leaks and engine disposed exactly once at the right time.

  ### Missing-plugin behavior (host-controlled)

  5. Default (RefuseDatabase) unchanged: open db with plugin index but without plugin; accessing that collection throws PLUGIN_REQUIRED.
  6. AllowIfSafe + read snapshot:
      - Open db without plugin but with AllowIfSafe; verify reading a non-plugin query plan doesn’t attempt plugin indexes (after planner fix).
  7. AllowIfSafe + write attempt:
      - Insert/update/delete into a collection that contains plugin-owned indexes throws PLUGIN_REQUIRED.

  ### Header marker + validation-on-open

  8. Create plugin index, verify Pragmas.HAS_PLUGIN_INDEXES == true.
  9. Enable ValidatePluginsOnOpen, open db without plugin and with strict policy; expect failure at construction time (fail-fast) once marker is set.

  ### $plugins

  10. With plugin present: $plugins lists pluginId, loaded=true, correct collections/indexCount.
  11. Without plugin and strict policy: $plugins still lists pluginId, loaded=false (because it bypasses validation checks while scanning).

  ### Rebuild drop option

  12. Seed file db with vector index:

  - db.Rebuild() without plugin throws (existing tests should still pass).
  - db.Rebuild(new RebuildOptions { DropOrphanedPluginIndexes = true }) succeeds without plugin.
  - Reopen with plugin and explicitly re-create vector index; ensure it works.

  Run verification:

  - dotnet test LiteDB.sln --settings tests.runsettings
  - Optional full matrix: pwsh -File scripts/run-tests-per-target.ps1

  ———

  ## Assumptions / Defaults

  - Default missing-plugin behavior remains PluginMissingBehavior.RefuseDatabase (no breaking change).
  - $plugins initially reports only plugin-owned indexes (not future plugin assets like arbitrary page allocations).
  - Header marker is sticky and used as an optimization; it does not need to be cleared automatically.
  - Shared-engine factory mode shares a single plugin context and initializes plugins exactly once for that context/engine pair.