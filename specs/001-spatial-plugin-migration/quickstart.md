# Quickstart – Spatial Plugin Migration

## 1. Update Dependencies
```bash
dotnet add package LiteDB --version <next-version-without-spatial>
dotnet add package LiteDB.Spatial --version <aligned-plugin-version>
```

- Target `netstandard2.0` (libraries) or `net8.0` (apps) and remove any direct references to the retired `LiteDB.Spatial` namespaces from core.
- Lock both packages to the same version so the plugin and core share the same surface area.

## 2. Declare Spatial Options

Annotate spatial members or configure them through the entity builder so the plugin can provision metadata when `EnsureIndex` is called.

### Attribute-based configuration
```csharp
public sealed class Place
{
    public int Id { get; set; }

    [SpatialOptions(
        Engine = SpatialEngineKind.Geographic2D,
        PrecisionBits = 40,
        DistanceMode = GeographicDistanceMode.Vincenty)]
    public GeoPoint Location { get; set; } = default!;
}
```

### Fluent mapper configuration
```csharp
BsonMapper.Global.Entity<Place>()
    .WithSpatialOptions(p => p.Location, options =>
    {
        options.UseEngine(SpatialEngineKind.Geographic2D);
        options.WithPrecisionBits(40);
        options.WithDistanceTolerance(15);
    });
```

- Declare a domain (`WithDomain`) for Cartesian datasets so the plugin can build Morton keys.
- Existing metadata stored in `_spatial_meta` remains valid and is reused automatically.

## 3. Enable the Spatial Plugin
```csharp
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Spatial;

var connection = new ConnectionString("Filename=MyData.db");
using var db = new LiteDatabase(connection, plugins: new ILitePlugin[]
{
    new SpatialPlugin()
});
```

- The plugin registers LINQ resolver factories, expression functions, planners, and the index interceptor at construction time.
- Compose multiple plugins by ordering them by priority; spatial works alongside vector search and other opt-in modules.

## 4. Provision Indexes via `EnsureIndex`
```csharp
var points = db.GetCollection<Place>("places");

// Triggers the spatial interceptor: metadata is created and indexes are built/backfilled.
points.EnsureIndex(x => x.Location);
```

- The interceptor inspects the mapped member, merges the declared options, writes `_spatial_meta`, and backfills `_idx`/`_mbb` as needed.
- If configuration is missing, the plugin logs actionable warnings and falls back to the core `EnsureIndex` behavior.

## 5. Query with `WhereNear`
```csharp
var center = new GeoPoint(48.8583, 2.2945);

var nearby = points.Query()
    .WhereNear(x => x.Location, center, radius: 500,
        distanceMode: GeographicDistanceMode.Haversine)
    .ToList();
```

- `WhereNear`, `WhereWithinBox`, and other helpers produce spatial expressions while keeping LINQ queries readable.
- Equivalent string/BsonExpression overloads allow dynamic field targeting when the geometry path is not strongly typed.

## 6. Verify Installation
```bash
dotnet test LiteDB.sln --settings tests.runsettings
dotnet test LiteDB.Spatial.Core.Tests
```

- Core solution tests should pass even if the plugin assembly is absent.
- Run spatial suites with the plugin referenced to validate the interceptor and query planner.

## 7. Migration Checklist
- Remove calls to `Spatial.Use*` helpers once `EnsureIndex` is guarded by the plugin.
- Confirm README/samples instruct consumers to register `SpatialPlugin`.
- Capture explain output with `docs/spatial-diagnostics.md` if query behavior changes.
