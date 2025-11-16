# Vector Plugin Isolation - Migration Guide

## Overview

Starting with LiteDB version 8.0+, vector search capabilities have been extracted from the core library into an optional plugin: `LiteDB.Vector`. This architectural change ensures that applications only pay for vector functionality when they need it, while keeping the core LiteDB library lean and focused.

## Breaking Changes

### For Prerelease Vector Users

**IMPORTANT**: Databases created with prerelease vector builds will require migration before vector operations work with the GA release.

When LiteDB detects plugin-owned assets (vector indexes, BSON types, or page types) but the `LiteDB.Vector` plugin is not registered, it will:

1. Log a single warning on database open
2. Allow the database to open successfully
3. Keep all non-vector collections fully accessible (read/write)
4. Throw `LiteException` (code `LITE2002`) when any vector operation is attempted

The error message will include:
- The plugin ID required (`LiteDB.Vector`)
- The collection/index identifiers affected
- Remediation steps

### API Changes

- **Removed from core**: All `Vector*` types, methods, and enums have been removed from the `LiteDB` namespace
- **Plugin registration required**: Applications must explicitly register `VectorSearchPlugin` to enable vector features
- **Extension methods**: Vector operations are now exposed via extension methods in the `LiteDB.Vector` package

## Migration Paths

### Recommended: Logical Export/Import

This is the safest migration path for prerelease users:

1. **Export your data** using the last prerelease build:
   ```csharp
   using var oldDb = new LiteDatabase("old-database.db");
   var allCollections = oldDb.GetCollectionNames();

   foreach (var collName in allCollections)
   {
       var collection = oldDb.GetCollection(collName);
       var documents = collection.FindAll();
       // Save documents to JSON or another format
   }
   ```

2. **Install GA packages**:
   ```bash
   dotnet add package LiteDB
   dotnet add package LiteDB.Vector
   ```

3. **Create a new database** with the plugin registered:
   ```csharp
   var options = new LiteDatabaseOptions
   {
       Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
   };

   using var newDb = new LiteDatabase("new-database.db", options);
   ```

4. **Re-insert documents** and recreate vector indexes:
   ```csharp
   var collection = newDb.GetCollection<MyDoc>("docs");
   collection.InsertBulk(documents);

   // Recreate vector indexes
   collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(384));
   ```

### Alternative: In-Place Index Rebuild

For databases where logical export is not feasible:

1. **Using the final prerelease build**, drop existing vector indexes:
   ```csharp
   using var db = new LiteDatabase("database.db");
   var collection = db.GetCollection<MyDoc>("docs");
   collection.DropIndex("embedding_idx");
   ```

2. **Upgrade to GA** and install `LiteDB.Vector`

3. **Register the plugin** and recreate indexes:
   ```csharp
   var options = new LiteDatabaseOptions
   {
       Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
   };

   using var db = new LiteDatabase("database.db", options);
   var collection = db.GetCollection<MyDoc>("docs");
   collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(384));
   ```

**Note**: GA releases cannot read prerelease metadata formats. Attempting vector operations will emit `LITE2002` until indexes are dropped and recreated.

## Plugin Registration

### Basic Usage

```csharp
using LiteDB;
using LiteDB.Vector;

var options = new LiteDatabaseOptions
{
    Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
};

using var db = new LiteDatabase("mydata.db", options);
```

### Connection String (if supported)

```csharp
var connectionString = "Filename=mydata.db;Plugins=LiteDB.Vector";
using var db = new LiteDatabase(connectionString);
```

## Using Vector Features

### Creating Vector Indexes

```csharp
using LiteDB.Vector;

var collection = db.GetCollection<Document>("documents");

// Create a vector index
var options = new VectorIndexOptions(
    dimensions: 384,
    metric: VectorMetric.Cosine
);

collection.EnsureIndex(x => x.Embedding, options);
```

### Querying with Vectors

```csharp
// Find similar documents
var queryVector = GetEmbedding("search query");
var similar = collection.Find(
    Query.Vector("embedding", queryVector, k: 10)
);

// Using BSON expressions
var results = collection.Find(
    "VECTOR_DIST(embedding, @vec) < 0.5",
    new BsonDocument { ["vec"] = queryVector }
);
```

## Missing Plugin Behavior

### What Happens Without the Plugin?

When you open a database containing vector data without registering `VectorSearchPlugin`:

1. **Database opens successfully** - no up-front errors
2. **Single warning logged** - informing you that plugin-owned metadata was detected
3. **Non-vector operations work** - standard CRUD, queries, and indexes function normally
4. **Vector operations fail** - with clear diagnostic messages

### Example Error Handling

```csharp
using var db = new LiteDatabase("database.db"); // No plugin registered

try
{
    var collection = db.GetCollection<MyDoc>("docs");
    collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(384));
}
catch (LiteException ex) when (ex.ErrorCode == 2002) // LITE2002
{
    Console.WriteLine($"Vector plugin required: {ex.Message}");

    if (ex.Data["PluginId"] is string pluginId)
    {
        Console.WriteLine($"Please install: {pluginId}");
    }

    if (ex.Data["VectorDiagnostics"] is BsonDocument diagnostics)
    {
        Console.WriteLine($"Diagnostics: {diagnostics}");
    }
}
```

## Advanced: Backup and Restore

### Logical Export (Recommended)

Always safe and preserves data integrity:

```csharp
// Export
var collections = db.GetCollectionNames();
foreach (var name in collections)
{
    var docs = db.GetCollection(name).FindAll();
    File.WriteAllText($"{name}.json", JsonSerializer.Serialize(docs));
}

// Import
foreach (var file in Directory.GetFiles(".", "*.json"))
{
    var collectionName = Path.GetFileNameWithoutExtension(file);
    var docs = JsonSerializer.Deserialize<BsonDocument[]>(File.ReadAllText(file));
    db.GetCollection(collectionName).InsertBulk(docs);
}
```

### Raw File Backup

Raw file backups are supported but maintain the "plugin required" state:

- Copying the `.db` file preserves all data and metadata
- Restoring still requires `LiteDB.Vector` to be registered before vector features work
- Non-vector data remains accessible without the plugin

### Compaction and Shrink

Operations that rewrite plugin-owned pages require the plugin:

```csharp
// With plugin registered - full compaction works
db.Rebuild();
db.Shrink();

// Without plugin - operation may be refused if plugin pages exist
try
{
    db.Rebuild();
}
catch (LiteException ex) when (ex.ErrorCode == 2002)
{
    Console.WriteLine("Cannot rebuild: plugin-owned pages require LiteDB.Vector");
}
```

## Reserved Identifier Ranges

To prevent conflicts between plugins, LiteDB reserves specific identifier ranges:

### BSON Type Codes

- **`0x90-0x9F`**: Reserved for LiteDB.Vector
  - `0x90`: Vector type

### Page Type Codes

- **`0xE0-0xEF`**: Reserved for LiteDB.Vector
  - `0xE0`: VectorIndexPage

### Index Kinds

- **`vector.hnsw`**: HNSW vector index
- **`vector.*`**: Reserved for future vector index types

## Custom Plugin Development

The same extensibility points used by `LiteDB.Vector` are available for custom plugins:

### Minimal Plugin Implementation

```csharp
public class MyPlugin : ILitePlugin
{
    public static readonly MyPlugin Instance = new MyPlugin();

    public void Initialize(LiteDatabase database, ILitePluginContext context)
    {
        // Register custom BSON types
        context.RegisterBsonType(new BsonTypeRegistration(
            pluginId: "MyPlugin",
            typeCode: 0xA0, // Choose from unreserved ranges
            name: "MyType",
            serializer: MySerializer.Write,
            deserializer: MySerializer.Read
        ));

        // Register page factories
        context.RegisterPageFactory(new PageFactoryRegistration(
            pluginId: "MyPlugin",
            pageType: "MyPageType",
            numericCode: 0xF0,
            compatibilityRange: ">=8.0",
            factory: ctx => new MyCustomPage(ctx.Buffer, ctx.PageId)
        ));

        // Register index strategies
        context.Indexes.Register(new MyIndexStrategy());
    }
}
```

### Plugin Context Services

Available services in `ILitePluginContext`:

- **Expressions**: Register custom SQL functions and operators
- **Indexes**: Register custom index strategies
- **QueryPlanner**: Add query planning rules
- **BsonTypes**: Register custom BSON serializers
- **PageFactories**: Register custom page types
- **Logger**: Access logging infrastructure
- **Services**: Access dependency injection container

## Troubleshooting

### Error: "Vector plugin required (LITE2002)"

**Cause**: Database contains vector data but `VectorSearchPlugin` is not registered.

**Solution**: Register the plugin during database construction:
```csharp
var options = new LiteDatabaseOptions
{
    Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
};
using var db = new LiteDatabase("mydata.db", options);
```

### Error: "BSON type code conflict"

**Cause**: Multiple plugins attempting to register the same BSON type code.

**Solution**: Ensure each plugin uses unique type codes from unreserved ranges. Check plugin documentation for reserved ranges.

### Error: "Legacy index needs rebuild"

**Cause**: Database contains prerelease vector indexes using old metadata format.

**Solution**: Follow one of the migration paths above (logical export/import or in-place rebuild).

### Performance Regression After Migration

**Expectation**: Vector query performance should be within 2% of prerelease builds.

**If experiencing degradation**:
1. Verify the plugin is properly registered
2. Check that indexes were recreated (not just copied)
3. Run benchmarks to quantify the regression
4. Report issues with benchmark results to the maintainers

## Performance Considerations

### Plugin Registration Overhead

- Plugin registration happens once during database construction
- Negligible performance impact (<1ms typically)
- No runtime overhead for non-vector operations

### Vector Query Performance

- Target: ≤2% regression compared to prerelease builds
- Actual impact depends on:
  - Index size and dimensionality
  - Query patterns
  - Hardware characteristics

### Build Size Impact

- **Core LiteDB**: Reduced by ~15-20% without vector code
- **LiteDB + LiteDB.Vector**: Approximately same total size as old integrated build
- **Applications using only core**: Benefit from smaller deployment size

## Version Compatibility

### Minimum Versions

- **LiteDB**: 8.0.0+
- **LiteDB.Vector**: 8.0.0+
- **.NET**: netstandard2.0, net462, net8.0+

### Version Matching

LiteDB.Vector should match the major.minor version of LiteDB:

| LiteDB Version | Compatible LiteDB.Vector |
|----------------|-------------------------|
| 8.0.x          | 8.0.x                   |
| 8.1.x          | 8.1.x                   |

Patch versions may vary independently.

## Support and Resources

### Documentation

- [LiteDB Documentation](https://www.litedb.org/docs/)
- [Vector Search Guide](https://www.litedb.org/docs/vector-search/)
- [Plugin Development Guide](https://www.litedb.org/docs/plugins/)

### Community

- [GitHub Issues](https://github.com/mbdavid/LiteDB/issues)
- [Discussions](https://github.com/mbdavid/LiteDB/discussions)

### Reporting Issues

When reporting vector-related issues, please include:

1. LiteDB version
2. LiteDB.Vector version
3. Whether the plugin is registered
4. Error messages including `LITE` error codes
5. Minimal reproduction code

## FAQ

**Q: Do I need to migrate if I've never used vector search?**

A: No. If your database has never used vector features, no migration is needed. Simply upgrade to the new version.

**Q: Can I use both old and new versions?**

A: No. Once you migrate to the plugin-based architecture, you cannot downgrade to prerelease builds that had integrated vector support.

**Q: Will vector features remain in a separate plugin?**

A: Yes. This is the permanent architecture going forward. Vector search is an optional feature provided by the `LiteDB.Vector` plugin.

**Q: Can I build my own custom index types using the same mechanism?**

A: Yes! The plugin system is designed to support custom index types, BSON types, page types, and query operators. See the Custom Plugin Development section above.

**Q: What happens if I forget to register the plugin in production?**

A: The database will open successfully, and non-vector operations will work normally. Vector operations will fail with clear error messages (`LITE2002`) indicating the missing plugin. You can deploy a fix by updating your code to register the plugin and restarting the application.

**Q: Is there a performance cost to the plugin architecture?**

A: The performance target is ≤2% regression for vector operations. Non-vector operations have zero overhead. Plugin registration itself adds <1ms to startup time.
