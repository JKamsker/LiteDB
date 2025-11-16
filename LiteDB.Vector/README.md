# LiteDB.Vector

Vector search plugin for LiteDB - Hierarchical Navigable Small World (HNSW) based nearest neighbor search.

## Overview

`LiteDB.Vector` is an optional plugin that adds vector similarity search capabilities to LiteDB. It enables efficient storage and querying of high-dimensional embeddings, making it ideal for semantic search, recommendation systems, and AI applications.

## Features

- 🚀 **High Performance**: HNSW algorithm for fast approximate nearest neighbor search
- 📏 **Multiple Metrics**: Support for Euclidean, Cosine, and Dot Product similarity
- 🔌 **Plugin Architecture**: Clean separation from core LiteDB - only included when needed
- 💾 **Persistent Storage**: Indexes are stored on disk and automatically loaded
- 🎯 **Type Safe**: Full C# type support with LINQ expressions

## Installation

```bash
dotnet add package LiteDB
dotnet add package LiteDB.Vector
```

## Quick Start

### 1. Register the Plugin

```csharp
using LiteDB;
using LiteDB.Vector;
using LiteDB.Vector.Extensions;

var options = new LiteDatabaseOptions
{
    Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
};

using var db = new LiteDatabase("mydata.db", options);
```

### 2. Define Your Document

```csharp
public class Document
{
    public int Id { get; set; }
    public string Text { get; set; }
    public float[] Embedding { get; set; }  // Your vector embeddings
}
```

### 3. Create a Vector Index

```csharp
var collection = db.GetCollection<Document>("documents");

// Create vector index with 384 dimensions using cosine similarity
collection.EnsureIndex(
    x => x.Embedding,
    new VectorIndexOptions(384, VectorDistanceMetric.Cosine)
);
```

### 4. Insert Documents

```csharp
collection.Insert(new Document
{
    Id = 1,
    Text = "Hello world",
    Embedding = GetEmbedding("Hello world")  // Your embedding function
});
```

### 5. Search by Similarity

```csharp
var queryVector = GetEmbedding("greeting message");

// Find 10 most similar documents
var similar = collection.Find(
    Query.Vector("Embedding", queryVector, k: 10)
);

foreach (var doc in similar)
{
    Console.WriteLine($"{doc.Text} - Distance: {doc.Distance}");
}
```

## Distance Metrics

### Cosine Similarity (Default)
Best for normalized vectors. Measures angle between vectors.

```csharp
new VectorIndexOptions(384, VectorDistanceMetric.Cosine)
```

### Euclidean Distance (L2)
Measures straight-line distance between points.

```csharp
new VectorIndexOptions(384, VectorDistanceMetric.Euclidean)
```

### Dot Product
Fast for normalized vectors, useful for certain embedding models.

```csharp
new VectorIndexOptions(384, VectorDistanceMetric.DotProduct)
```

## API Reference

### Extension Methods

All vector operations are exposed through extension methods on `ILiteCollection<T>`:

```csharp
// Create named index
bool EnsureIndex<T>(
    this ILiteCollection<T> collection,
    string name,
    BsonExpression expression,
    VectorIndexOptions options)

// Create auto-named index
bool EnsureIndex<T>(
    this ILiteCollection<T> collection,
    BsonExpression expression,
    VectorIndexOptions options)

// Create index using lambda
bool EnsureIndex<T, K>(
    this ILiteCollection<T> collection,
    Expression<Func<T, K>> keySelector,
    VectorIndexOptions options)

// Create named index using lambda
bool EnsureIndex<T, K>(
    this ILiteCollection<T> collection,
    string name,
    Expression<Func<T, K>> keySelector,
    VectorIndexOptions options)
```

### VectorIndexOptions

```csharp
public class VectorIndexOptions
{
    public ushort Dimensions { get; }        // Vector dimensionality (e.g., 384, 768, 1536)
    public VectorDistanceMetric Metric { get; } // Distance metric to use

    public VectorIndexOptions(
        ushort dimensions,
        VectorDistanceMetric metric = VectorDistanceMetric.Cosine)
}
```

## Advanced Usage

### Combining with Other Filters

```csharp
// Find similar documents that match additional criteria
var results = collection.Find(
    Query.And(
        Query.Vector("Embedding", queryVector, k: 20),
        Query.GTE("PublishedDate", DateTime.Now.AddDays(-30))
    )
);
```

### Multiple Vector Indexes

```csharp
public class MultiModalDocument
{
    public int Id { get; set; }
    public float[] TextEmbedding { get; set; }
    public float[] ImageEmbedding { get; set; }
}

var collection = db.GetCollection<MultiModalDocument>("multimodal");

// Create separate indexes for each embedding type
collection.EnsureIndex(
    x => x.TextEmbedding,
    new VectorIndexOptions(768, VectorDistanceMetric.Cosine)
);

collection.EnsureIndex(
    x => x.ImageEmbedding,
    new VectorIndexOptions(512, VectorDistanceMetric.Cosine)
);
```

## Migration from Prerelease

If you used vector search in prerelease versions of LiteDB:

1. **Export your data** using the prerelease version
2. **Upgrade** to GA release and install `LiteDB.Vector`
3. **Register the plugin** in your database initialization
4. **Reimport data** and recreate vector indexes

See the [Migration Guide](../docs/vector-plugin-isolation.md) for detailed instructions.

## Error Handling

### Plugin Not Registered

If you forget to register the plugin, you'll get a clear error:

```csharp
try
{
    collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(384));
}
catch (LiteException ex) when (ex.ErrorCode == 2002)
{
    Console.WriteLine("LiteDB.Vector plugin required!");
    Console.WriteLine($"Plugin ID: {ex.Data["PluginId"]}");
    Console.WriteLine($"Solution: {ex.Data["Solution"]}");
}
```

Error code `LITE2002` indicates a missing plugin dependency.

## Performance Tips

1. **Choose appropriate dimensions**: More dimensions = more storage and slower queries
2. **Normalize vectors**: For cosine similarity, pre-normalize vectors for better performance
3. **Batch insertions**: Use `InsertBulk()` for better index building performance
4. **Index parameters**: HNSW parameters are auto-tuned but can be customized in future versions

## Limitations

- Maximum vector dimensions: 65,535 (ushort.MaxValue)
- HNSW parameters are currently fixed (M=16, efConstruction=200)
- Approximate search only (exact k-NN not guaranteed)

## Compatibility

- **.NET Standard 2.0+** (compatible with .NET Framework 4.6.1+)
- **.NET 8.0+**
- Requires **LiteDB 8.0+**

## License

MIT License - Same as LiteDB core

## Contributing

This plugin is part of the LiteDB project. See the main LiteDB repository for contribution guidelines.

## Support

- 📖 [Documentation](https://www.litedb.org/docs/vector-search/)
- 💬 [Discussions](https://github.com/mbdavid/LiteDB/discussions)
- 🐛 [Issues](https://github.com/mbdavid/LiteDB/issues)
