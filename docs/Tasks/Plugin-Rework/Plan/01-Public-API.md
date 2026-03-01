# Public API changes (additive)

## 1) LiteDatabaseBuilder (new)

File: `LiteDB/Client/Database/LiteDatabaseBuilder.cs`  
Namespace: `LiteDB`

Proposed surface (fluent, returns this):

Plugin registration (store factories internally):

- `UsePlugin(ILitePlugin plugin)`
- `UsePlugin<TPlugin>() where TPlugin : ILitePlugin, new()`
- `UsePlugin(Func<ILitePlugin> pluginFactory)`
- `UsePlugins(IEnumerable<ILitePlugin> plugins)`

Data source (mutually exclusive, last call wins):

- `UseFile(string filename)` (shorthand for `UseConnectionString("Filename=...")`)
- `UseConnectionString(string connectionString)`
- `UseConnectionString(ConnectionString connectionString)`
- `UseInMemory()` (shorthand for `UseConnectionString(":memory:")`)
- `UseStream(Stream dataStream, Stream logStream = null)` (mirrors existing ctor semantics)
- `UseEngine(ILiteEngine engine, bool disposeOnFactoryDispose = true)`
  - Note: when `UseEngine(...)` is chosen, `BuildFactory()` must use shared-engine mode (see below).

Configuration:

- `WithMapper(BsonMapper mapper)`
- `WithServices(IServiceProvider services)`
- `WithLogger(ILogger logger)`
- `WithPassword(string password)` (applies to connection-string/engine settings where relevant)
- `AsReadOnly()` (connection string `ReadOnly` / engine settings)
- `WithConnectionType(ConnectionType type)` (Direct vs `ConnectionType.Shared` mutex mode)
- `ConfigureEngine(Action<EngineSettings> configure)` (passed into `ConnectionString.CreateEngine`)
- `WithMissingPluginBehavior(PluginMissingBehavior behavior)` (host-controlled, see below)
- `ValidatePluginsOnOpen(bool enabled = true)` (opt-in global scan)
- `WithEngineReuse(EngineReuse reuse)` (for `BuildFactory()`, see below)

Build:

- `ILiteDatabase Build()`
- `ILiteDatabaseFactory BuildFactory()`

## 2) Engine reuse toggle for factories (new)

File: `LiteDB/Client/Database/EngineReuse.cs`  
Namespace: `LiteDB`

public enum EngineReuse
{
    None = 0,   // config-only factory
    Shared = 1  // shared-engine factory (in-process reuse)
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

Behavior modes (driven by builder’s `WithEngineReuse(...)`):

- `EngineReuse.None`: each `CreateDatabase()` creates:
  - new engine (from connection string/stream settings),
  - new `DefaultPluginContext`,
  - initializes plugins once for that database.
- `EngineReuse.Shared`: factory owns:
  - one engine instance,
  - one plugin context instance,
  - one “plugin host” `LiteDatabase` instance used only for plugin initialization (kept alive for factory lifetime),
  - ref-counted “client” `LiteDatabase` wrappers returned from `CreateDatabase()` that share engine/context and do not initialize plugins.

## 4) Host-controlled missing plugin behavior

File: `LiteDB/Client/Database/LiteDatabaseOptions.cs` (modify)

Add:

- `public PluginMissingBehavior MissingPluginBehavior { get; set; } = PluginMissingBehavior.RefuseDatabase;`
- `public bool ValidatePluginsOnOpen { get; set; } = false;` (optional; builder sets this too)

Builder writes these options regardless of whether user uses the builder or constructors. Existing constructors + `LiteDatabaseOptions` should honor the new fields.

## 5) Rebuild option: drop orphaned plugin indexes (new)

File: `LiteDB/Engine/Structures/RebuildOptions.cs` (modify)

Add:

public bool DropOrphanedPluginIndexes { get; set; } = false;
