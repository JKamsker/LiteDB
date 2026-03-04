# LiteDB.Vector Migration Guide

The vector search implementation now ships as a plugin instead of living in the LiteDB core library. Follow the steps below to migrate existing applications and understand how to register the plugin.

## 1. Install the Package

Add a reference to the extension package in every project that uses vector search:

```bash
dotnet add package LiteDB.Vector
```

## 2. Register the Plugin

Pass the plugin instance when constructing `LiteDatabase`. This enables vector indexes, expressions, and query planning:

```csharp
using var db = new LiteDatabase(
    "Filename=mydata.db",
    plugins: new[] { VectorSearchPlugin.Instance });
```

If you omit this step the runtime will throw the error message:

```
Vector operations require the LiteDB.Vector plugin. Install the LiteDB.Vector package and register VectorSearchPlugin.Instance when constructing LiteDatabase ...
```

Re-run the constructor with `VectorSearchPlugin.Instance` to resolve it.

## 3. Verify Existing Code

All public APIs keep their signatures. After registering the plugin you can continue to use:

- `collection.EnsureIndex(x => x.Embedding, options)`
- `collection.Query().TopKNear(...).WithVectorScore()`
- `VECTOR_DIST` and `VECTOR_SIM` expressions

These LINQ helpers now live in `LiteDB.Vector.LiteQueryableVectorExtensions`, so add `using LiteDB.Vector;` to any file that calls `WhereNear`, `TopKNear`, `FindNearest`, or `WithVectorScore`. The base `LiteQueryable` class no longer exposes those members directly.

Existing database files remain compatible because on-disk structures still live in the core library.

## 4. Optional: Default Metric via Connection String

You can configure a default metric for all indexes using the connection string key `vector.metric`:

```csharp
using var db = new LiteDatabase(
    "Filename=mydata.db;vector.metric=cosine",
    plugins: new[] { VectorSearchPlugin.Instance });
```

## 5. Troubleshooting Checklist

- Seeing "Vector operations require the LiteDB.Vector plugin..."? Ensure the package is referenced and the plugin array is supplied when constructing `LiteDatabase`.
- Missing namespace? Add `using LiteDB.Vector;` to files that work with vector APIs.
- Query dimensionality mismatch? Confirm the vector length passed to `TopKNear` matches the index dimensions configured in `VectorIndexOptions`.

Following these steps keeps existing vector workloads running while benefiting from the new plugin-based architecture.
