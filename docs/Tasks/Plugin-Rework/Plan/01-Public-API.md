# Public API changes (additive)

## 1) LiteDatabaseBuilder (new)

File: `LiteDB/Client/Database/LiteDatabaseBuilder.cs`  
Namespace: `LiteDB`

Proposed surface (fluent, returns this):

Plugin registration (store factories internally):

- `UsePlugin(ILitePlugin plugin)`
- `UsePlugin<TPlugin>() where TPlugin : ILitePlugin, new()` (no DI; convenience only)
- `UsePlugin(Func<ILitePlugin> pluginFactory)` (no DI; convenience only)
- `UsePlugin(Func<IServiceProvider, ILitePlugin> pluginFactory)` (DI-friendly)
- `UsePlugins(IEnumerable<ILitePlugin> plugins)`

Plugin lifetime notes:

- `UsePlugin(ILitePlugin plugin)` reuses the same instance for every database/handle; prefer factories for stateful plugins.
- Plugin factories are invoked:
  - `FactoryReuse.None`: once per `CreateDatabase()`
  - `FactoryReuse.ReuseEngine`: once per factory (when the shared engine/context is created)

Data source (mutually exclusive, last call wins):

- `UseFile(string filename)` (sets `ConnectionString.Filename` directly; no string concatenation)
- `UseConnectionString(string connectionString)`
- `UseConnectionString(ConnectionString connectionString)`
- `UseInMemory()` (shorthand for `UseConnectionString(":memory:")`)
- `UseStream(Stream dataStream, Stream logStream = null)` (mirrors existing ctor semantics)
- `UseEngine(ILiteEngine engine, bool disposeOnFactoryDispose = true)`
  - Note: when `UseEngine(...)` is chosen, `BuildFactory(reuse: ...)` must use `FactoryReuse.ReuseEngine` (see below).

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
- Builder validates incompatible combinations (for example: `UseEngine(...)` + `WithConnectionType(...)`).

## 2) Factory reuse toggle for factories (new)

File: `LiteDB/Client/Database/FactoryReuse.cs`  
Namespace: `LiteDB`

public enum FactoryReuse
{
    None = 0,   // config-only factory
    ReuseEngine = 1  // shared in-process engine/context
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

Behavior modes (driven by builder’s `BuildFactory(reuse: ...)`):

- `FactoryReuse.None`: each `CreateDatabase()` creates:
  - new engine (from connection string/stream settings),
  - new `DefaultPluginContext`,
  - initializes plugins once for that database.
- `FactoryReuse.ReuseEngine`: factory owns:
  - one engine instance (or one `ILiteEngine` wrapper, e.g. `SharedEngine`),
  - one plugin context instance,
  - initializes plugins exactly once for that context/engine pair,
  - returns ref-counted `LiteDatabase` handles that share engine/context and do not re-initialize plugins.

Important: because `ILitePlugin.Initialize` receives a `LiteDatabase` instance, shared-engine reuse requires a documented constraint that plugin initialization is registration-only and must not capture the passed database instance (future work may add an explicit per-session hook if needed).

## 4) Host-controlled missing plugin behavior

File: `LiteDB/Client/Database/LiteDatabaseOptions.cs` (modify)

Add:

- `public PluginMissingBehavior MissingPluginBehavior { get; set; } = PluginMissingBehavior.RefuseDatabase;`
- `public bool ValidatePluginsOnOpen { get; set; } = false;` (optional; builder sets this too)

Builder writes these options regardless of whether user uses the builder or constructors. Existing constructors + `LiteDatabaseOptions` should honor the new fields.

Precedence:

- `LiteDatabaseOptions.MissingPluginBehavior` is a host override and is authoritative.
- Plugin diagnostic policies may customize exception messages/diagnostics, but must not weaken host enforcement (engine uses the stricter of host behavior and plugin policy).

## 5) Rebuild option: drop orphaned plugin indexes (new)

File: `LiteDB/Engine/Structures/RebuildOptions.cs` (modify)

Add:

public bool DropOrphanedPluginIndexes { get; set; } = false;
