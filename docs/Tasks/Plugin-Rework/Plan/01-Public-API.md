# Public API changes (additive)

## 1) LiteDatabaseBuilder (new)

File: `LiteDB/Client/Database/LiteDatabaseBuilder.cs`  
Namespace: `LiteDB`

Proposed surface (fluent, returns this):

Plugin registration (store factories internally):

- `UsePlugin(ILitePlugin plugin)`
- `UsePlugin<TPlugin>() where TPlugin : ILitePlugin, new()` (no DI; convenience only)
- `UsePlugin(Func<ILitePlugin> pluginFactory)` (no DI; convenience only)
- `UsePlugin(Func<IServiceProvider, ILitePlugin> pluginFactory)` (DI-friendly; receives the provider set via `WithServices(...)`, otherwise an empty provider)
- `UsePlugins(IEnumerable<ILitePlugin> plugins)`

Plugin lifetime notes:

- `UsePlugin(...)` calls append; registration order matters.
- `UsePlugin(ILitePlugin plugin)` reuses the same instance for every database/handle; prefer factories for stateful plugins.
- In `FactoryReuse.None`, `UsePlugin(ILitePlugin plugin)` can initialize the same instance multiple times (once per `CreateDatabase()`); plugin `Initialize(...)` should be idempotent or prefer plugin factories.
- Plugin initialization is de-duped by plugin CLR type (first registered instance wins), mirroring existing `LiteDatabase` constructor behavior.
- Plugin factories are invoked:
  - `FactoryReuse.None`: once per `CreateDatabase()`
  - `FactoryReuse.ReuseEngine`: once per factory (when the reused engine/context is created)
- Plugin instances are not disposed by the database/factory in this iteration; if a plugin needs disposal, the host must model/manage that explicitly.
- In `FactoryReuse.ReuseEngine`, plugin factories run once per factory; avoid scoped-service assumptions unless the host builds factories per scope.
- In `FactoryReuse.ReuseEngine`, the plugin instance produced by a factory is effectively a singleton for the lifetime of the factory/engine pair; plugin state must be thread-safe.

Data source (mutually exclusive, last call wins):

- `UseFile(string filename)` (sets `ConnectionString.Filename` directly; no string concatenation)
- `UseConnectionString(string connectionString)`
- `UseConnectionString(ConnectionString connectionString)`
- `UseInMemory()` (shorthand for `UseConnectionString(":memory:")`)
- `UseStream(Stream dataStream, Stream logStream = null)` (mirrors existing ctor semantics)
- `UseEngine(ILiteEngine engine, bool disposeOnFactoryDispose = true)`
  - Ownership: when `disposeOnFactoryDispose=true`, the created database/factory owns the engine and disposes it (for `Build()`: when the returned `LiteDatabase` is disposed; for `BuildFactory()`: when the last lease is released). When false, the host owns engine disposal.
  - Note: when `UseEngine(...)` is chosen, `BuildFactory(reuse: ...)` must use `FactoryReuse.ReuseEngine` (see below).
  - Note: plugin registrations only affect storage/query behavior when the supplied engine honors `IPluginHost.SetPluginContext` (as `LiteEngine`/`SharedEngine` do).
  - Note: when `UseEngine(...)` is chosen, the plugin context `ConnectionString` is synthetic/empty unless the host supplies an explicit context connection string; plugins must not assume filename/read-only flags are available from the context in this mode.

Configuration:

- `WithMapper(BsonMapper mapper)`
- `WithServices(IServiceProvider services)`
- `WithLogger(ILogger logger)`
- `WithPassword(string password)` (applies to connection-string/engine settings where relevant)
- `AsReadOnly()` (connection string `ReadOnly` / engine settings)
- `WithConnectionType(ConnectionType type)` (maps to `ConnectionString.Connection` / `connection=` key; Direct vs `ConnectionType.Shared` mutex mode)
- `ConfigureEngine(Action<EngineSettings> configure)` (passed into `ConnectionString.CreateEngine`)
- `WithMissingPluginBehavior(PluginMissingBehavior behavior)` (host-controlled, see below)
- `ValidatePluginsOnOpen(bool enabled = true)` (opt-in global scan)
- `ConfigureOptions(Action<LiteDatabaseOptions> configure)` (escape hatch for future options)

Build:

- `ILiteDatabase Build()`
- `ILiteDatabaseFactory BuildFactory(FactoryReuse reuse = FactoryReuse.None)`

Notes:

- `BuildFactory(...)` is not supported for `UseStream(Stream ...)` unless we introduce a stream factory (e.g. `UseStream(Func<(Stream Data, Stream Log)> openStreams)`), because a single stream instance cannot safely back multiple databases.
- `UseInMemory()` semantics: `FactoryReuse.None` creates a new empty in-memory database per `CreateDatabase()`; `FactoryReuse.ReuseEngine` returns many handles to the same in-memory database.
- Builder validates incompatible combinations (for example: `UseEngine(...)` + `WithConnectionType(...)`).

## 2) Factory reuse toggle for factories (new)

File: `LiteDB/Client/Database/FactoryReuse.cs`  
Namespace: `LiteDB`

public enum FactoryReuse
{
    None = 0,   // config-only factory
    ReuseEngine = 1  // reused in-process engine/context
}

## 3) ILiteDatabaseFactory + LiteDatabaseFactory (new)

Files:

- `LiteDB/Client/Database/ILiteDatabaseFactory.cs`
- `LiteDB/Client/Database/LiteDatabaseFactory.cs`

Interface:

public interface ILiteDatabaseFactory : IDisposable
{
    ILiteDatabase CreateDatabase();
}

Disposal + concurrency contract:

- `CreateDatabase()` must be thread-safe.
- After factory disposal, `CreateDatabase()` throws.
- Disposing the factory must not invalidate existing handles; they remain usable until disposed.

Behavior modes (driven by builder’s `BuildFactory(reuse: ...)`):

- `FactoryReuse.None`: each `CreateDatabase()` creates:
  - new engine (from connection string/stream settings),
  - new `DefaultPluginContext`,
  - initializes plugins once for that database.
  - Note: for file-backed databases, `FactoryReuse.None` can create multiple independent engines for the same file; if handles may be alive concurrently, require `ConnectionType.Shared` (or prefer `FactoryReuse.ReuseEngine`) to avoid locking/corruption risks.
- `FactoryReuse.ReuseEngine`: factory owns:
  - one engine instance (or one `ILiteEngine` wrapper, e.g. `SharedEngine`),
  - one plugin context instance,
  - initializes plugins exactly once for that context/engine pair,
  - returns ref-counted `LiteDatabase` handles that share engine/context and do not re-initialize plugins (handles are leases, not isolated “sessions”; they share engine state and per-thread transactions).

Important: because `ILitePlugin.Initialize` receives a `LiteDatabase` instance, reused-engine mode requires a documented constraint that plugin initialization is registration-only and must not capture the passed database instance (future work may add an explicit per-handle/session hook if needed).

## 4) Host-controlled missing plugin behavior

File: `LiteDB/Client/Database/LiteDatabaseOptions.cs` (modify)

Add:

- `public PluginMissingBehavior MissingPluginBehavior { get; set; } = PluginMissingBehavior.RefuseDatabase;`
- `public bool ValidatePluginsOnOpen { get; set; } = false;` (optional; builder sets this too)

Existing constructors + `LiteDatabaseOptions` should honor the new fields; the builder sets them when used.

Precedence:

- `LiteDatabaseOptions.MissingPluginBehavior` is the enforcement source of truth (host-controlled).
- Plugin diagnostic policies (`IPluginDiagnosticPolicy`) may customize exception messages/diagnostics, but must not influence enforcement (its `MissingBehavior` is ignored for enforcement in this iteration).

## 5) Rebuild option: drop orphaned plugin indexes (new)

File: `LiteDB/Engine/Structures/RebuildOptions.cs` (modify)

Add:

public bool DropOrphanedPluginIndexes { get; set; } = false;
