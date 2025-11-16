# Plugin Context Lifecycle (Design Notes)

## Current Design Overview
- `LiteDatabase` builds a fresh `DefaultPluginContext` for every database instance (connection string or stream constructors).  
- Each plugin in `IEnumerable<ILitePlugin>` receives `Initialize(LiteDatabase db, ILitePluginContext context)` so it can touch both the database surface and the registries exposed by the context.  
- After initialization, the database passes the context to whatever engine it hosts via `IPluginHost.SetPluginContext`. Engines only ever see the context; they never hold a reference back to `LiteDatabase`.  
- Engine helpers that need expensive derived data (page factory lookup tables, BSON type registries, etc.) keep static `ConditionalWeakTable<ILitePluginContext, …>` caches. Those tables lazily build the derived objects once per context and automatically drop entries when the context is garbage collected.  
- A `SharedEngine` (or any custom `ILiteEngine`) can be wrapped by multiple `LiteDatabase` instances; each wrapper can choose its own plugin set while sharing the same engine because the context travels through `SetPluginContext`.

## Pain Points With the Current Approach
- Static caches make ownership implicit. It is hard to reason about when entries disappear because eviction depends entirely on GC.  
- Profiling or leak diagnosis is tricky—if a context stays rooted, the weak table entry sticks around with no diagnostics.  
- Environments with strict policies on static state cannot opt out.  
- Advanced hosts/tests cannot reset caches deterministically; they must drop the whole context and wait for GC.  
- The pattern is duplicated (page factories, BSON registry, potentially more), so any change requires touching several `ConditionalWeakTable` wrappers.

## Lifecycle API Proposal (to eliminate static caches)

### Context Construction
- Introduce `IPluginContextFactory`. Default implementation returns `DefaultPluginContext`.  
- Hosts (`LiteDatabase`, `SharedEngine`, direct `LiteEngine`) must call the factory to obtain contexts rather than instantiating them ad hoc.

### Ownership & Disposal
- `ILitePluginContext` implements `IDisposable` and exposes `event EventHandler Disposing`.  
- Whoever creates the context is responsible for calling `Dispose()` when the database/engine shuts down.  
- Consumers can subscribe to `Disposing` to tear down derived state.

### Registry Accessors
- Move caches inside the context and expose methods such as `PageFactoryRegistry GetOrCreatePageFactoryRegistry()` and `BsonTypeRegistry GetOrCreateBsonTypeRegistry()`.  
- Implementations provide thread-safe lazy initialization (e.g., `Lazy<T>` or internal locks).  
- Engine code replaces static resolver calls with `context?.GetOrCreate…() ?? ImmutableDefaultContext.Get…()`.

### Sharing Rules
- Callers may reuse a single context across multiple `LiteDatabase` wrappers, but all parties must coordinate disposal.  
- `LiteDatabase` constructors accept an optional `ILitePluginContext` parameter; if omitted, they create and own one.  
- When wrapping an external engine, callers can provide a context instance so both the engine and database share explicit ownership.

### Engine Contract
- `IPluginHost.SetPluginContext` remains, but hosts validate that the context has not been disposed.  
- Hosts subscribe to `Disposing` to clear any cached pointers immediately instead of waiting for GC.  
- Engines no longer rely on static caches; they ask the context instance for registries on demand.

### Default / Plugin-Free Context
- Replace the current `_defaultContext` singleton with an `ImmutablePluginContext`.  
- This immutable context lives for the lifetime of the process, exposes read-only registries, and bypasses disposal semantics.  
- Callers passing `null` contexts receive this immutable instance automatically.

## Migration Steps
1. Implement `IPluginContextFactory` and extend `ILitePluginContext` with `IDisposable` plus the registry accessors.  
2. Update `DefaultPluginContext` to own all derived registries internally and surface thread-safe `GetOrCreate…` helpers.  
3. Change `LiteDatabase`, `SharedEngine`, and `LiteEngine` to accept optional factories/contexts and to dispose of owned contexts deterministically.  
4. Replace `PageFactoryResolver`/`BsonTypeResolver` static caches with thin wrappers that delegate to the context’s accessors.  
5. Provide diagnostics (debug logging or analyzer) that warn when contexts are not disposed or when disposed contexts are reused.  
6. Update tests and plugins to pass contexts explicitly where they previously relied on static resolvers.

### Example: Owning a Context per Database
```csharp
var factory = new DefaultPluginContextFactory();

await using var context = factory.Create(connectionString, services, logger);
context.RegisterPageFactory(MyPageFactoryRegistration);

await using var engine = new LiteEngine(settings);
((IPluginHost)engine).SetPluginContext(context);

await using var database = new LiteDatabase(engine, context);
database.Services.Logger.Write(LogLevel.Information, "Initialized with custom context.");
```

### Example: Sharing a Context Across Databases
```csharp
await using var sharedContext = factory.Create(connectionString, services, logger);
sharedContext.RegisterCustomIndexStrategy(VectorPlugin.Strategy);

await using var engine = new LiteEngine(settings);
((IPluginHost)engine).SetPluginContext(sharedContext);

await using var dbWriter = new LiteDatabase(engine, sharedContext);
await using var dbReader = new LiteDatabase(engine, sharedContext);
```

### Example: Engine Requesting Registries
```csharp
public sealed class VectorPageFactoryRule
{
    public BasePage Create(PageBuffer buffer, ILitePluginContext context)
    {
        var registry = context.GetOrCreatePageFactoryRegistry();
        if (!registry.TryGetRegistration(PageType.VectorIndex, out var registration))
        {
            throw VectorCompatibility.PluginRequired();
        }

        return (BasePage)registration.Factory(new PageConstructionContext(
            context,
            buffer,
            PageType.VectorIndex,
            buffer.ReadUInt32(BasePage.P_PAGE_ID),
            isNewPage: false));
    }
}
```

## Trade-offs
- **Pros**: Explicit ownership, deterministic cleanup, easier reasoning about lifetime, no static weak tables, hosts can enforce security policies.  
- **Cons**: Every caller must manage context disposal correctly (risk of premature disposal/leaks), API surface grows, custom hosts/tests must adapt, more boilerplate for simple scenarios.  
- **Performance**: Slight overhead per context for storing caches, but eliminates the small lookup cost in `ConditionalWeakTable`.  
- **Safety**: Requires clear guidance and tooling to ensure contexts are not disposed while still in use.

This document captures both the current design and a possible lifecycle API if the repository chooses to drop `ConditionalWeakTable`-based caches while maintaining safety and performance guarantees.

