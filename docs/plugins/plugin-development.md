# LiteDB Plugin Development Guide

LiteDB now exposes a formal plugin surface so that feature packs (vector search, spatial indexing, diagnostics, etc.) can bolt into the database pipeline without forking core code. This guide walks through the infrastructure introduced on the `feat/plugins/core` branch, shows how the spatial revamp uses it on `feat/spatial-revamp-plugins`, and outlines the steps required to ship your own plugin.

## Lifecycle & Initialization

- A `LiteDatabase` is constructed with an optional `IEnumerable<ILitePlugin>`; each distinct plugin type receives a single `Initialize` call with a per-database `ILitePluginContext` (`LiteDB/Client/Database/LiteDatabase.cs:43`, `LiteDB/Client/Database/LiteDatabase.cs:174`).
- The context materializes the extension registries and exposes shared services such as dependency injection and logging (`LiteDB/Plugins/DefaultPluginContext.cs:9`).
- After all plugins run, the database hands the context to internal engine components via `IPluginHost.SetPluginContext`, so low-level operations can query plugin registries during inserts, updates, query planning, etc. (`LiteDB/Client/Database/LiteDatabase.cs:196`, `LiteDB/Engine/LiteEngine.cs:41`).
- The same context is surfaced back to application code through `LiteDatabase.Services`, giving callers readonly access to the registries and helpers (`LiteDB/Client/Database/LiteDatabaseServices.cs:19`).

> **Idempotency:** The initializer guards against duplicate registration by tracking plugin types; use plugin-level state or the supplied service provider if you need cross-call coordination (`LiteDB/Client/Database/LiteDatabase.cs:176`).

## Registry Catalog

All registries live under `ILitePluginContext` (`LiteDB/Plugins/ILitePlugin.cs:23`). They are thread-safe and can be accessed both during initialization and at runtime via `LiteDatabase.Services`.

### Expression Registry

- Register SQL/BSON operators, functions, and keywords (`LiteDB/Plugins/DefaultPluginContext.cs:38`).
- Expressions created anywhere in the client or engine now consult the active registry, which means custom functions work in LINQ, SQL text, and document queries (`LiteDB/Document/Expression/BsonExpression.cs:327`, `LiteDB/Utils/Tokenizer.cs:172`).
- Operators and keywords influence tokenization, allowing plugins to add new syntax like `SPATIAL_NEAR` (`LiteDB/Utils/Tokenizer.cs:200`).

### Index Registry & Strategies

- Implement `IIndexStrategy` to describe a custom index type (`LiteDB/Plugins/ILitePlugin.cs:88`).
- Register strategies with a stable kind and byte code; the engine enumerates `Indexes.All` whenever documents are inserted, updated, deleted, or rebuilt and calls the strategy hooks accordingly (`LiteDB/Engine/Engine/Insert.cs:31`, `LiteDB/Engine/Engine/Update.cs:32`, `LiteDB/Engine/Engine/Delete.cs:22`, `LiteDB/Engine/Engine/Rebuild.cs:67`).
- Use `EnsureIndex` to delegate index creation and `DropIndex` for cleanup. For example, the vector pipeline fetches a strategy by kind before building a vector index (`LiteDB/Engine/Engine/Index.cs:102`).

### Query Planner Registry

- `IQueryPlanningRule` lets a plugin participate in index selection (`LiteDB/Plugins/ILitePlugin.cs:118`).
- `QueryOptimization` asks each registered rule (ordered) to rewrite the plan; a rule must call `context.UseIndex` to take ownership (`LiteDB/Engine/Query/QueryOptimization.cs:173`).
- The context exposes the current snapshot, query terms, and helper slots for residual filters, enabling advanced planners like spatial range searches.

### Query Metadata Accessor

- Register a metadata descriptor per plugin with `context.RegisterQueryMetadata(pluginId, version, reservedKeys)`; descriptors are routed through `IQueryMetadataAccessor` (`LiteDB/Plugins/Query/IQueryMetadataAccessor.cs`).
- `QueryOptimization` and other engine paths call `Query.GetOrCreateMetadata`, so planning rules can retrieve a strongly typed `QueryMetadataBag` via `context.GetOrCreateMetadata(pluginId, factory)` (`LiteDB/Plugins/QueryPlanningContext.cs:74`).
- Bags let you stash planner state (target embeddings, scoring hints, etc.) outside the `Query` object while enforcing reserved-key validation and versioning (`LiteDB/Plugins/Query/QueryMetadataBag.cs:14`).

```csharp
public override bool TryRewrite(QueryPlanningContext context)
{
    var bag = context.GetOrCreateMetadata(VectorQueryMetadata.PluginId);
    bag.Set(VectorQueryMetadata.TargetEmbedding, target);
    // Emit filters or call context.UseIndex(...)
}
```

Use `LiteDatabase.Services.QueryMetadata` when application code needs to inspect descriptors or emit diagnostics after initialization (`LiteDB/Client/Database/LiteDatabaseServices.cs:32`).

### BSON Type Registry

- Plugins reserve type codes and serializers by calling `context.RegisterBsonType(new BsonTypeRegistration(...))` during initialization (`LiteDB/Plugins/Bson/IBsonTypeRegistry.cs`).
- Type codes ≥128 keep core enums stable while allowing plugins to round-trip `ValueTask`-based serialization handlers (`LiteDB/Document/Bson/BsonTypeRegistry.cs:14`).
- The registry feeds every BSON serialization path (`LiteDB/Document/BsonValue.cs`, `LiteDB/Document/Json/JsonWriter.cs`), so once a plugin registers a type, all writers/readers automatically delegate to the supplied delegates.

```csharp
context.RegisterBsonType(new BsonTypeRegistration(
    pluginId: "LiteDB.Vector",
    typeCode: 200,
    name: "Vector128",
    serializer: VectorBsonSerializer.SerializeAsync,
    deserializer: VectorBsonSerializer.DeserializeAsync,
    legacyAliases: new byte[] { (byte)BsonType.Vector }));
```

Fallback registrations keep legacy documents readable, but new writes should use the plugin-owned code path to avoid reintroducing core dependencies.

### Page Factory Registry

- Storage extensions register page constructors via `context.RegisterPageFactory(new PageFactoryRegistration(...))` (`LiteDB/Plugins/Storage/IPageFactoryRegistry.cs`).
- Each registration declares a logical `pageType` and compatibility range so the engine can validate formats before `FileReaderV8` and `SnapShot` materialize pages (`LiteDB/Engine/Pages/PageFactoryRegistry.cs`, `LiteDB/Engine/FileReader/FileReaderV8.cs:52`).
- Optional metadata serializers and rebuild hooks participate in checkpoints and `LiteDB/Engine/Engine/Rebuild.cs`, allowing plugins to persist auxiliary page headers and coordinate recovery.

```csharp
context.RegisterPageFactory(new PageFactoryRegistration(
    pluginId: "LiteDB.Vector",
    pageType: "VectorIndex",
    compatibilityRange: ">=8.0",
    factory: VectorPageFactory.Create,
    metadataSerializer: VectorPageFactory.SerializeMetadataAsync,
    rebuildHook: VectorPageFactory.OnRebuildAsync));
```

When a page type is registered, `LiteDatabaseServices` swaps the default fallback resolver so every page allocation/clone defers to the plugin without friend assemblies (`LiteDB/Client/Database/LiteDatabaseServices.cs:17`).

### LINQ Resolver Registry

- `ILinqResolverRegistry.Register` wires `MethodInfo` / `MemberInfo` patterns to BSON expressions on a per-type basis (`LiteDB/Plugins/ILitePlugin.cs:145`).
- The LINQ visitor consults plugin resolvers before falling back to built-ins, and per-database resolver instances are cached for reuse (`LiteDB/Client/Mapper/Linq/LinqExpressionVisitor.cs:761`).
- Use this hook to translate strong-typed helpers (e.g., `SpatialExpressions.Near`) into the functions you registered with the expression registry.

### Index Interceptor Registry

- Interceptors execute during `ILiteCollection<T>.EnsureIndex` before the engine sees the request (`LiteDB/Client/Database/Collections/Index.cs:167`).
- `EnsureIndexContext` carries the entity type, mapper, database, engine, and helper methods. An interceptor may adjust parameters, call `ExecuteDefault`, and/or short-circuit with a custom result (`LiteDB/Plugins/EnsureIndexContext.cs:106`, `LiteDB/Plugins/EnsureIndexContext.cs:135`).
- Use the `order` parameter when registering to control precedence (`LiteDB/Plugins/ILitePlugin.cs:164`).

## Expressions & Tokenization Pipeline

Custom expression artifacts propagate automatically:

- Plugin functions flow into SQL text, shell commands, programmatic expressions, and LINQ resolutions because all entry points call `BsonExpression.Create(..., registry)` (`LiteDB/Client/Database/LiteCollection.cs:137`, `LiteDB/Client/Database/LiteDatabase.cs:304`).
- Tokenization treats plugin keywords and operators as operands, letting you introduce new infix/postfix constructs without altering parser source (`LiteDB/Utils/Tokenizer.cs:200`).

## Logging, Services, and Configuration

- The plugin context exposes an `ILogger` abstraction so you can emit structured messages without depending on LiteDB’s internal logger (`LiteDB/Plugins/ILitePlugin.cs:45`).
- `Services` and `ConnectionString` give you a DI hook and configuration surface. For example, dependency injection can provide cloud SDK clients to a plugin at initialization time.
- `LiteDatabase.Services` exposes a default context for scenarios that need registry access without an active plugin (`LiteDB/Client/Database/LiteDatabaseServices.cs:61`).

## Spatial Plugin Case Study

The spatial revamp demonstrates how a complex feature composes the registries:

1. **Initialization:** `SpatialPlugin.Initialize` attaches plugin services to the database, registers spatial expression functions, LINQ resolvers, a query planning rule, and an index interceptor (`LiteDB.Spatial/Plugin/SpatialPlugin.cs:19`).
2. **State Management:** `SpatialPluginRegistry` keeps a per-database `SpatialPluginServices` instance in a `ConditionalWeakTable`, avoiding manual disposal while providing shared caches (`LiteDB.Spatial/Plugin/SpatialPluginRegistry.cs:12`).
3. **Expression Surface:** Functions such as `SPATIAL_NEAR` are added to the expression registry and mirrored into the global default for convenience (`LiteDB.Spatial/Plugin/SpatialPlugin.cs:85`).
4. **LINQ Integration:** LINQ resolvers translate `SpatialExpressions` method calls into the registered functions via the registry (`LiteDB.Spatial/Plugin/SpatialPlugin.cs:28`).
5. **EnsureIndex Interception:** `SpatialPluginServices.TryHandleEnsureIndex` recognizes spatial field paths, ensures supporting B-tree indexes, rebuilds metadata, and signals success through `context.SetResult(true)` without hitting the default engine path (`LiteDB.Spatial/Plugin/SpatialPluginServices.cs:50`).
6. **Query Planning:** `SpatialQueryPlanningRule` scans query terms for spatial predicates, resolves metadata, and injects a custom `SpatialMultiRangeIndex` into the plan via `context.UseIndex` (`LiteDB.Spatial/Plugin/QueryPlanning/SpatialQueryPlanningRule.cs:25`).
7. **Diagnostics:** The plugin uses the shared logger to surface configuration issues and ships a static `LogDiagnostics` helper for runtime audits (`LiteDB.Spatial/Plugin/SpatialPlugin.cs:36`).

## Implementation Checklist

1. **Define the Plugin Class.** Implement `ILitePlugin` and prepare any services you need (cache, metadata store, DI populator).
2. **Register Expression Surface.** Add keywords/operators/functions required for SQL and LINQ usage.
3. **Bridge LINQ.** Register resolvers for any extension methods or helper types you expect consumers to call.
4. **Install Index Strategies or Interceptors.**
   - Use `IndexRegistry.Register` plus an `IIndexStrategy` implementation if you need a new on-disk index type.
   - Use `IndexInterceptors.Register` when you want to hijack or extend the default `EnsureIndex` workflow.
5. **Hook Query Planning.** Optionally add `IQueryPlanningRule` implementations to control index choice, returning residual filters when necessary.
6. **Leverage Services.** Read the connection string for configuration, resolve DI services, and log status or warnings.
7. **Persist State Carefully.** Prefer per-database caches (e.g., `ConditionalWeakTable`) so multiple `LiteDatabase` instances do not bleed state across each other.
8. **Expose Diagnostics.** Provide a public helper that verifies prerequisite metadata and logs actionable guidance, similar to `SpatialPlugin.LogDiagnostics`.

### Skeleton Example

```csharp
public sealed class SamplePlugin : ILitePlugin
{
    public void Initialize(LiteDatabase database, ILitePluginContext context)
    {
        context.Expressions.RegisterFunction(
            "SAMPLE_FN",
            SampleFunctions.Invoke,
            BsonExpressionType.Call,
            convertScalarLeftToEnumerable: false,
            isScalarResult: true);

        context.LinqResolvers.Register(typeof(SampleExpressions), db => new SampleResolver(db));

        context.QueryPlanner.AddRule(new SamplePlanningRule(context), order: 200);

        context.IndexInterceptors.Register(ctx =>
        {
            if (!IsSampleIndex(ctx.Expression))
            {
                return false;
            }

            // Optional: run the default handler with adjusted parameters.
            ctx.ExecuteDefault(name: ctx.Name + "_sample");
            ctx.SetResult(true);
            return true;
        });
    }
}
```

## Enabling Plugins in Applications

```csharp
var plugins = new ILitePlugin[]
{
    new SpatialPlugin(),          // from LiteDB.Spatial
    new SamplePlugin()            // your custom feature
};

using var db = new LiteDatabase("Filename=my.db", plugins: plugins);
```

You can query the registries at runtime to inspect installed plugins:

```csharp
foreach (var function in db.Services.ExpressionRegistry.Functions)
{
    Console.WriteLine(function.Name);
}
```

## Testing & Diagnostics

- Unit test individual registries (e.g., ensure expression functions behave as expected) and integration test by executing queries through `LiteDatabase`.
- If your plugin alters index planning, add regression tests similar to `LiteDB.Spatial.Core.Tests/Plugin/SpatialPluginIntegrationTests.cs:121`.
- Provide CLI tooling or shell commands that call your diagnostics helper so operators can verify configuration in production.

With these extension points you can introduce rich features while keeping the core engine stable. Study the spatial plugin for advanced patterns, and follow the checklist above to deliver a predictable, testable plugin experience.
