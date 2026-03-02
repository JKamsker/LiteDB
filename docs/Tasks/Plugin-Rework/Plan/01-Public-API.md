# Public API changes (additive)

## 1) LiteDatabaseBuilder (new)

File: `LiteDB/Client/Database/LiteDatabaseBuilder.cs`
Namespace: `LiteDB`

Proposed surface (fluent, returns this):

Plugin registration (store factories internally):

- [x] `UsePlugin(ILitePlugin plugin)` -- reuses the same instance; caller retains ownership (not disposed by builder/database/factory)
- [x] `UsePlugin<TPlugin>() where TPlugin : ILitePlugin, new()` (no DI; convenience only)
- [x] `UsePlugin(Func<ILitePlugin> pluginFactory)` -- factory-created instances are disposed by the owning database/factory if they implement `IDisposable`
- [x] `UsePlugin(Func<IServiceProvider, ILitePlugin> pluginFactory)` (DI-friendly; receives the provider set via `WithServices(...)`, otherwise an empty provider)
- [x] `UsePlugin<TPlugin>(Func<IServiceProvider, TPlugin> pluginFactory) where TPlugin : ILitePlugin` (typed factory; avoids constructing then discarding duplicates when de-duping by CLR type)
- [x] `UsePlugins(IEnumerable<ILitePlugin> plugins)` / `UsePlugins(params ILitePlugin[] plugins)`

Plugin lifetime notes:

- [x] `UsePlugin(...)` calls append; registration order matters.
- [x] `UsePlugin(ILitePlugin plugin)` reuses the same instance; the builder/database/factory does NOT dispose it. Caller owns lifetime.
- [x] `UsePlugin(Func<...>)` factories: the created instance IS disposed by the database/factory when the owning scope ends (if it implements `IDisposable`).
- [x] Plugin initialization is de-duped by plugin CLR type (first registered instance wins), mirroring existing `LiteDatabase` constructor behavior. **Duplicate registrations of the same CLR type log a warning** rather than being silently ignored.
  - Note: warning-on-duplicates is a builder/factory diagnostic; existing constructors can remain silent for compatibility.
- [x] Plugin factories are invoked:
  - [x] `Build()`: once per `Build()` call
  - [x] `BuildFactory()`: once per factory (when the shared engine/context is created)
- [x] In factory mode, the plugin instance produced by a factory is effectively a singleton for the lifetime of the factory/engine pair; plugin state must be thread-safe.
- [x] In factory mode, plugin factories run once per factory; avoid scoped-service assumptions unless the host builds factories per scope.

Data source (mutually exclusive; **second call throws `InvalidOperationException`**, not "last call wins"):

- [x] `UseFile(string filename)` (sets `ConnectionString.Filename` directly; no string concatenation)
- [x] `UseConnectionString(string connectionString)`
- [x] `UseConnectionString(ConnectionString connectionString)`
- [x] `UseInMemory()` (shorthand for `UseConnectionString(":memory:")`)
- [x] `UseTemp()` (shorthand for `UseConnectionString(":temp:")`)
- [x] `UseStream(Stream dataStream, Stream logStream = null)` (mirrors existing ctor semantics)
- [x] `UseEngine(ILiteEngine engine, bool ownsEngine = true)`
  - Ownership: when `ownsEngine=true`, the created database/factory owns the engine and disposes it (for `Build()`: when the returned `LiteDatabase` is disposed; for `BuildFactory()`: when the last lease is released). When false, the host owns engine disposal.
  - Note: when `UseEngine(...)` is chosen, `BuildFactory()` reuses the provided engine directly (ref-counted handles).
  - Note: plugin registrations only affect storage/query behavior when the supplied engine honors `IPluginHost.SetPluginContext` (as `LiteEngine`/`SharedEngine` do).
  - Note: when `UseEngine(...)` is chosen, the plugin context `ConnectionString` is synthetic/empty unless the host supplies an explicit context connection string via `WithContextConnectionString(...)`.

Configuration:

- [x] `WithMapper(BsonMapper mapper)`
- [x] `WithServices(IServiceProvider services)`
- [x] `WithLogger(ILogger logger)`
- [x] `WithContextConnectionString(ConnectionString connectionString)` (used to supply a real connection string to the plugin context when `UseEngine(...)` is selected; copy at build time, do not retain a mutable reference)
- [x] `WithPassword(string password)` (applies to connection-string/engine settings where relevant)
- [x] `AsReadOnly()` (connection string `ReadOnly` / engine settings)
- [x] `WithConnectionType(ConnectionType type)` (maps to `ConnectionString.Connection` / `connection=` key; Direct vs `ConnectionType.Shared` mutex mode)
- [x] `ConfigureEngine(Action<EngineSettings> configure)` (passed into `ConnectionString.CreateEngine`)
- [x] `WithMissingPluginBehavior(PluginMissingBehavior behavior)` (host-controlled, see below)
- [x] `ValidatePluginsOnOpen(bool enabled = true)` (opt-in; defaults to disabled unless explicitly enabled, preserving legacy behavior)
- [x] `ConfigureOptions(Action<LiteDatabaseOptions> configure)` (escape hatch for future options; applied in call order/last-call-wins)

Build:

- [x] `ILiteDatabase Build()` -- creates a single database
- [x] `ILiteDatabaseFactory BuildFactory()` -- creates a shared-engine factory (always reuses engine; ref-counted handles)

Builder is single-use: calling `Build()` or `BuildFactory()` more than once throws `InvalidOperationException`.

Builder is NOT thread-safe (same convention as `IHostBuilder`, `DbContextOptionsBuilder`).

Notes:

- [x] All `With*` / `Configure*` calls are applied in call order (last call wins).
- [x] `BuildFactory()` is not supported for `UseStream(Stream ...)` (throws; stream lifetime/ownership and checkpoint override semantics are hard to make safe across ref-counted handles). If needed, use `UseEngine(...)` with a pre-created engine, or add a future `UseStreamFactory(...)` overload.
- [x] `UseInMemory()` + `BuildFactory()` returns many handles to the same in-memory database.
- [x] Builder validates incompatible combinations at build time:
  - [x] `UseEngine(...)` + `WithConnectionType(...)` → throws
  - [x] `UseStream(...)` + `BuildFactory()` → throws
  - [x] `WithContextConnectionString(...)` without `UseEngine(...)` → throws
  - Note: `UseFile(...)` + `BuildFactory()` + `ConnectionType.Direct` (the default) is safe and recommended. The factory creates ONE engine (exclusive file access) with multiple in-process handles -- no corruption risk. `ConnectionType.Shared` is only needed when *multiple processes* access the same file, not for in-process handle sharing.

## 2) ILiteDatabaseFactory + LiteDatabaseFactory (new)

Files:

- `LiteDB/Client/Database/ILiteDatabaseFactory.cs`
- `LiteDB/Client/Database/LiteDatabaseFactory.cs`

Interface:

```csharp
public interface ILiteDatabaseFactory : IDisposable
{
    ILiteDatabase CreateDatabase();
    bool IsDisposed { get; }
}
```

Disposal + concurrency contract:

- [x] `CreateDatabase()` must be thread-safe.
- [x] After factory disposal, `CreateDatabase()` throws `ObjectDisposedException`.
- [x] Disposing the factory must not invalidate existing handles; they remain usable until disposed.
- [x] `CreateDatabase()` uses `Interlocked.Increment` for refcount, then checks disposed state; if disposed, decrements and throws.

Factory behavior (always shared-engine):

- [x] Factory owns one engine instance (or one `ILiteEngine` wrapper, e.g. `SharedEngine`), one plugin context instance.
- [x] Initializes plugins exactly once for that context/engine pair.
- [x] Returns ref-counted `LiteDatabase` handles that share engine/context and do not re-initialize plugins (handles are leases, not isolated "sessions"; they share engine state and per-thread transactions).
- [x] The reused engine is disposed when the last reference is released (factory disposed + all handles disposed).
- [x] The internal "plugin host" used for initialization must not own a reference (does not count toward refcount).
- [x] Handle disposal uses `Interlocked.CompareExchange` to ensure idempotency (double-dispose is safe, never underflows refcount).

Important: `ILitePlugin.Initialize` receives a `LiteDatabase` instance. See Intention.md for the resolution plan (option a: change signature, or option b: add per-handle hook).

## 3) Host-controlled missing plugin behavior

File: `LiteDB/Client/Database/LiteDatabaseOptions.cs` (modify)

Add:

- [x] `public PluginMissingBehavior MissingPluginBehavior { get; set; } = PluginMissingBehavior.RefuseDatabase;`
- [x] `public bool? ValidatePluginsOnOpen { get; set; } = null;`
  - When `null` (default), the effective value is `false` (legacy behavior; do not fail-fast on open unless explicitly enabled).
  - When explicitly set to `true` or `false`, that value is used regardless of `MissingPluginBehavior`.
  - The builder should leave this `null` unless `ValidatePluginsOnOpen(...)` is called.

Existing constructors + `LiteDatabaseOptions` should honor the new fields; the builder sets them when used.

Precedence:

- [x] `LiteDatabaseOptions.MissingPluginBehavior` is the enforcement source of truth (host-controlled).
- [x] `IPluginDiagnosticPolicy.MissingBehavior` is **deprecated** (marked `[Obsolete]`). It is ignored for enforcement. Plugin diagnostic policies can still customize exception messages via `CreateMissingPluginException`, but cannot influence the enforcement decision.
- [x] Compatibility note: any consumer/plugin relying on `IPluginDiagnosticPolicy.MissingBehavior != RefuseDatabase` to relax enforcement must now set `LiteDatabaseOptions.MissingPluginBehavior` explicitly.
- [x] The host-controlled `MissingPluginBehavior` must propagate from `LiteDatabaseOptions` → `DefaultPluginContext` (or a new field on `ILitePluginContext`) → `Snapshot` constructor. This is a new plumbing path that does not exist today.

PluginMissingBehavior enum:

```csharp
public enum PluginMissingBehavior
{
    RefuseDatabase = 0,  // strict default; throw on any affected collection access
    [Obsolete("Use AllowIfSafe instead. RefuseOperations is treated as AllowIfSafe.")]
    RefuseOperations = 1, // kept for binary compatibility; treated as AllowIfSafe at runtime
    AllowIfSafe = 2       // allow reads on affected collections; refuse writes/DDL
}
```

Migration note: `RefuseOperations` (value `1`) is deprecated but retained at its original ordinal for binary compatibility with existing compiled code. At runtime, `RefuseOperations` is treated identically to `AllowIfSafe`. The enum member is marked `[Obsolete]` to steer new code toward `AllowIfSafe`. Phase 1 must search for all `RefuseOperations` references in the codebase and map them to `AllowIfSafe`.

## 4) Rebuild option: drop orphaned plugin indexes (new)

File: `LiteDB/Engine/Structures/RebuildOptions.cs` (modify)

Add:

```csharp
public bool DropOrphanedPluginIndexes { get; set; } = false;
```

Validation: `DropOrphanedPluginIndexes=true` with `IncludeErrorReport=false` throws `ArgumentException` (audit trail required).

## 5) Diagnostic policy cleanup

- [x] Remove hard-coded `"LiteDB.Vector"` message from `DefaultPluginDiagnosticPolicy` (core must be plugin-agnostic).
- [x] Mark `IPluginDiagnosticPolicy.MissingBehavior` as `[Obsolete("Use LiteDatabaseOptions.MissingPluginBehavior instead. This property is ignored for enforcement.")]`.
- [x] Plugin packages that want to provide plugin-specific guidance should do so via `CreateMissingPluginException` message customization, not via `MissingBehavior`.

## 6) LinqResolverFactory delegate constraint

The `LinqResolverFactory` delegate (`ILitePlugin.cs` line 198) accepts `LiteDatabase database` as a parameter -- the same class of problem as `ILitePlugin.Initialize`. In factory mode, the `LiteDatabase` passed to the resolver factory may be the internal initialization host, not a user-facing handle. Plugins must not capture this reference. Whatever resolution is chosen for `Initialize` (option a or b from Intention.md) must also apply to `LinqResolverFactory`. Currently the Spatial plugin ignores the parameter (`_ => services.GetOrCreateResolver(...)`), but the delegate signature allows future plugins to capture it. Document this as a factory-mode constraint, and consider revising the delegate signature alongside `Initialize`.

## 7) MissingPluginBehavior propagation path

The host-controlled `MissingPluginBehavior` must flow from `LiteDatabaseOptions` into the `Snapshot` constructor where enforcement occurs. Three options (choose one during implementation):

- [x] (a) Add `MissingPluginBehavior` to `ILitePluginContext` (breaking change for implementors of the interface)
- [x] (b) Add `MissingPluginBehavior` to `DefaultPluginContext` only, cast in `Snapshot` (fragile)
- [x] (c) Pass `MissingPluginBehavior` separately to `Snapshot` via the engine/transaction path (cleanest; no interface change)

Option (c) is preferred: the `MissingPluginBehavior` can be stored on `LiteEngine` alongside `_plugins` and threaded into `TransactionMonitor` → `Transaction` → `Snapshot`.
