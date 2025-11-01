# Quickstart – Spatial Plugin Migration

## 1. Update Dependencies
```bash
dotnet add package LiteDB --version <next-version-without-spatial>
dotnet add package LiteDB.Spatial --version <aligned-plugin-version>
```

- Ensure the application targets `netstandard2.0` or later (for libraries) and `net8.0` where applicable.
- Remove any direct references to `LiteDB/Spatial` namespaces migrated from earlier prototypes.

## 2. Enable the Spatial Plugin
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

- The plugin registers spatial LINQ resolvers, expression functions, and index strategies during `LiteDatabase` construction.
- If additional plugins (e.g., vector search) are used, order them according to desired planning priority.

## 3. Configure Collections
```csharp
var collection = db.GetCollection<PointDocument>("points");

collection.EnsureIndex(x => x.Location); // Spatial plugin intercepts GeoPoint indexes
collection.Insert(new PointDocument { Id = 1, Location = new GeoPoint(48.8583, 2.2945) });

var nearby = collection.Query()
    .WhereNear(x => x.Location, new GeoPoint(48.8583, 2.2945), 500)
    .ToList();
```

- The spatial plugin registers an index interceptor that recognizes `GeoPoint` (and related) types and routes `EnsureIndex` calls to the spatial index builder—no additional helpers required.
- LINQ extension methods such as `WhereNear` wrap the underlying expressions to avoid namespace collisions while `SpatialExpressions.*` remains available for advanced scenarios.
- Without the plugin, the query throws a descriptive `LiteException` requesting plugin enablement.

## 4. Verify Installation
```bash
dotnet test LiteDB.sln --settings tests.runsettings
dotnet test LiteDB.Spatial.Core.Tests
```

- Core solution tests should pass with the plugin absent.
- Spatial test suites should be executed with the plugin referenced to validate functional parity.

## 5. Review Documentation
- Reference updated docs under `docs/spatial-*.md` for migration notes and limitations.
- Ensure release notes call out the plugin dependency and enabling instructions.
