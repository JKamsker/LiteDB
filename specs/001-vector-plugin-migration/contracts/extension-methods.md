# Extension Methods API Contract

**Version**: 1.0  
**Package**: LiteDB.Vector  
**Namespace**: LiteDB.Vector

## Overview

This document defines the public API for vector extension methods that provide a fluent interface for vector search operations. All methods are extension methods on core LiteDB types.

---

## LiteCollectionVectorExtensions

Extension methods for `ILiteCollection<T>` to create and manage vector indexes.

### EnsureIndex (Named, Expression)

Creates a vector index with an explicit name and BsonExpression.

**Signature**:
```csharp
public static bool EnsureIndex<T>(
    this ILiteCollection<T> collection,
    string name,
    BsonExpression expression,
    VectorIndexOptions options)
```

**Parameters**:
- `collection`: The collection to index
- `name`: Unique index name within the collection
- `expression`: BsonExpression selecting the vector field
- `options`: Vector-specific configuration (dimensions, metric)

**Returns**:
- `true`: Index was created
- `false`: Index already exists with identical configuration

**Exceptions**:
- `ArgumentNullException`: If any parameter is null
- `ArgumentException`: If name is empty or whitespace
- `LiteException`: If index exists with different configuration
- `LiteException`: If vector plugin not registered

**Example**:
```csharp
var options = new VectorIndexOptions(384, VectorDistanceMetric.Cosine);
collection.EnsureIndex("embedding_idx", "$.Embedding", options);
```

---

### EnsureIndex (Expression)

Creates a vector index with auto-generated name from expression.

**Signature**:
```csharp
public static bool EnsureIndex<T>(
    this ILiteCollection<T> collection,
    BsonExpression expression,
    VectorIndexOptions options)
```

**Parameters**:
- `collection`: The collection to index
- `expression`: BsonExpression selecting the vector field
- `options`: Vector configuration

**Index Name**: Auto-generated from expression (e.g., `"$.Embedding"` → `"Embedding"`)

**Returns**: Same as named version

**Example**:
```csharp
var options = new VectorIndexOptions(768, VectorDistanceMetric.Euclidean);
collection.EnsureIndex("$.Vector", options);
```

---

### EnsureIndex (Lambda, Named)

Creates a vector index using a strongly-typed lambda expression with explicit name.

**Signature**:
```csharp
public static bool EnsureIndex<T, K>(
    this ILiteCollection<T> collection,
    string name,
    Expression<Func<T, K>> keySelector,
    VectorIndexOptions options)
```

**Parameters**:
- `collection`: The collection to index
- `name`: Unique index name
- `keySelector`: Lambda expression selecting the vector property
- `options`: Vector configuration

**Returns**: Same as expression version

**Example**:
```csharp
public class Document
{
    public int Id { get; set; }
    public float[] Embedding { get; set; }
}

var options = new VectorIndexOptions(512);
collection.EnsureIndex("emb_idx", x => x.Embedding, options);
```

---

### EnsureIndex (Lambda)

Creates a vector index using a lambda with auto-generated name.

**Signature**:
```csharp
public static bool EnsureIndex<T, K>(
    this ILiteCollection<T> collection,
    Expression<Func<T, K>> keySelector,
    VectorIndexOptions options)
```

**Parameters**:
- `collection`: The collection to index
- `keySelector`: Lambda expression
- `options`: Vector configuration

**Index Name**: Derived from property name (e.g., `x => x.Embedding` → `"Embedding"`)

**Example**:
```csharp
var options = new VectorIndexOptions(256, VectorDistanceMetric.DotProduct);
collection.EnsureIndex(x => x.Vector, options);
```

---

## LiteQueryableVectorExtensions

Extension methods for `ILiteQueryable<T>` to perform vector similarity searches.

### WhereNear (String Field)

Filters documents where vector field is within distance threshold.

**Signature**:
```csharp
public static ILiteQueryable<T> WhereNear<T>(
    this ILiteQueryable<T> source,
    string vectorField,
    float[] target,
    double maxDistance)
```

**Parameters**:
- `source`: Queryable source
- `vectorField`: Name of the vector field (e.g., `"Embedding"`)
- `target`: Query vector
- `maxDistance`: Maximum distance threshold (inclusive)

**Returns**: Filtered queryable (composable with other LINQ methods)

**Behavior**:
- Uses vector index if available on the field
- Falls back to full scan if no index exists
- Distance calculation uses index's configured metric

**Example**:
```csharp
var queryVector = new float[] { 0.1f, 0.2f, 0.3f, ... };
var similar = collection
    .Query()
    .WhereNear("Embedding", queryVector, maxDistance: 0.5)
    .Where(x => x.Published == true)
    .ToList();
```

---

### WhereNear (BsonExpression)

Filters using a BsonExpression to identify the vector field.

**Signature**:
```csharp
public static ILiteQueryable<T> WhereNear<T>(
    this ILiteQueryable<T> source,
    BsonExpression fieldExpr,
    float[] target,
    double maxDistance)
```

**Parameters**:
- `source`: Queryable source
- `fieldExpr`: Expression selecting vector field (e.g., `"$.Nested.Vector"`)
- `target`: Query vector
- `maxDistance`: Distance threshold

**Returns**: Filtered queryable

**Example**:
```csharp
var similar = collection
    .Query()
    .WhereNear("$.Metadata.Embedding", queryVector, 0.8)
    .Limit(10)
    .ToList();
```

---

### WhereNear (Lambda)

Filters using a strongly-typed lambda expression.

**Signature**:
```csharp
public static ILiteQueryable<T> WhereNear<T, K>(
    this ILiteQueryable<T> source,
    Expression<Func<T, K>> field,
    float[] target,
    double maxDistance)
```

**Parameters**:
- `source`: Queryable source
- `field`: Lambda selecting vector property
- `target`: Query vector  
- `maxDistance`: Distance threshold

**Returns**: Filtered queryable

**Example**:
```csharp
var similar = collection
    .Query()
    .WhereNear(x => x.Embedding, queryVector, maxDistance: 0.6)
    .OrderBy(x => x.CreatedDate)
    .ToList();
```

---

### FindNearest (String Field)

Convenience method to execute WhereNear and return results immediately.

**Signature**:
```csharp
public static IEnumerable<T> FindNearest<T>(
    this ILiteQueryable<T> source,
    string vectorField,
    float[] target,
    double maxDistance)
```

**Parameters**:
- `source`: Queryable source
- `vectorField`: Vector field name
- `target`: Query vector
- `maxDistance`: Distance threshold

**Returns**: Enumerable of matching documents (eager evaluation)

**Example**:
```csharp
var nearest = collection
    .Query()
    .FindNearest("Embedding", queryVector, maxDistance: 0.5);

foreach (var doc in nearest)
{
    Console.WriteLine(doc.Id);
}
```

---

### TopKNear (Lambda)

Finds the K nearest neighbors using a lambda selector.

**Signature**:
```csharp
public static ILiteQueryableResult<T> TopKNear<T, K>(
    this ILiteQueryable<T> source,
    Expression<Func<T, K>> field,
    float[] target,
    int k)
```

**Parameters**:
- `source`: Queryable source
- `field`: Lambda selecting vector property
- `target`: Query vector
- `k`: Number of nearest neighbors to return

**Returns**: ILiteQueryableResult with top-k results ordered by distance

**Behavior**:
- Internally sets maxDistance to infinity
- Limits results to k documents
- Returns results ordered by ascending distance

**Example**:
```csharp
var top10 = collection
    .Query()
    .TopKNear(x => x.Embedding, queryVector, k: 10)
    .ToList();

// Results are automatically sorted by distance (closest first)
```

---

### TopKNear (String Field)

Finds K nearest neighbors using a string field name.

**Signature**:
```csharp
public static ILiteQueryableResult<T> TopKNear<T>(
    this ILiteQueryable<T> source,
    string field,
    float[] target,
    int k)
```

**Parameters**:
- `source`: Queryable source
- `field`: Vector field name
- `target`: Query vector
- `k`: Number of neighbors

**Returns**: Top-k results ordered by distance

**Example**:
```csharp
var top5 = collection
    .Query()
    .TopKNear("Vector", queryVector, 5)
    .ToList();
```

---

### TopKNear (BsonExpression)

Finds K nearest neighbors using a BsonExpression.

**Signature**:
```csharp
public static ILiteQueryableResult<T> TopKNear<T>(
    this ILiteQueryable<T> source,
    BsonExpression fieldExpr,
    float[] target,
    int k)
```

**Parameters**:
- `source`: Queryable source
- `fieldExpr`: Expression selecting vector field
- `target`: Query vector
- `k`: Number of neighbors

**Returns**: Top-k results ordered by distance

**Example**:
```csharp
var top20 = collection
    .Query()
    .TopKNear("$.Features.Embedding", queryVector, 20)
    .ToList();
```

---

## LiteRepositoryVectorExtensions

Extension methods for `ILiteRepository` to manage vector indexes across collections.

### EnsureIndex (Named, Expression)

Creates a vector index on a repository-managed collection.

**Signature**:
```csharp
public static bool EnsureIndex<T>(
    this ILiteRepository repository,
    string name,
    BsonExpression expression,
    VectorIndexOptions options,
    string collectionName = null)
```

**Parameters**:
- `repository`: The repository instance
- `name`: Index name
- `expression`: Field selector
- `options`: Vector configuration
- `collectionName`: Optional collection name (defaults to T's type name)

**Returns**: True if created, false if already exists

**Example**:
```csharp
var options = new VectorIndexOptions(384);
repository.EnsureIndex<Document>("vec_idx", "$.Embedding", options);
```

---

### EnsureIndex (Expression)

Creates a vector index with auto-generated name.

**Signature**:
```csharp
public static bool EnsureIndex<T>(
    this ILiteRepository repository,
    BsonExpression expression,
    VectorIndexOptions options,
    string collectionName = null)
```

**Parameters**:
- `repository`: The repository instance
- `expression`: Field selector
- `options`: Vector configuration
- `collectionName`: Optional collection override

**Example**:
```csharp
var options = new VectorIndexOptions(512, VectorDistanceMetric.Euclidean);
repository.EnsureIndex<Product>("$.Features", options);
```

---

### EnsureIndex (Lambda)

Creates a vector index using a strongly-typed lambda with auto-generated name.

**Signature**:
```csharp
public static bool EnsureIndex<T, K>(
    this ILiteRepository repository,
    Expression<Func<T, K>> keySelector,
    VectorIndexOptions options,
    string collectionName = null)
```

**Parameters**:
- `repository`: The repository instance
- `keySelector`: Property selector
- `options`: Vector configuration
- `collectionName`: Optional collection override

**Example**:
```csharp
var options = new VectorIndexOptions(768);
repository.EnsureIndex<Article>(x => x.Embedding, options);
```

---

### EnsureIndex (Lambda, Named)

Creates a vector index with explicit name using a lambda.

**Signature**:
```csharp
public static bool EnsureIndex<T, K>(
    this ILiteRepository repository,
    string name,
    Expression<Func<T, K>> keySelector,
    VectorIndexOptions options,
    string collectionName = null)
```

**Parameters**:
- `repository`: The repository instance
- `name`: Index name
- `keySelector`: Property selector
- `options`: Vector configuration
- `collectionName`: Optional collection override

**Example**:
```csharp
var options = new VectorIndexOptions(256, VectorDistanceMetric.DotProduct);
repository.EnsureIndex<Image>("img_vec", x => x.Vector, options);
```

---

## VectorIndexOptions

Configuration class for vector index creation.

**Signature**:
```csharp
public sealed class VectorIndexOptions
{
    public ushort Dimensions { get; }
    public VectorDistanceMetric Metric { get; }
    
    public VectorIndexOptions(ushort dimensions, 
                             VectorDistanceMetric metric = VectorDistanceMetric.Cosine)
}
```

**Properties**:
- `Dimensions`: Vector dimensionality (1-65535)
- `Metric`: Distance calculation method (default: Cosine)

**Constructor Validation**:
- Throws `ArgumentOutOfRangeException` if dimensions = 0

**Example**:
```csharp
// Default cosine metric
var options1 = new VectorIndexOptions(384);

// Explicit euclidean metric
var options2 = new VectorIndexOptions(512, VectorDistanceMetric.Euclidean);

// Dot product metric
var options3 = new VectorIndexOptions(768, VectorDistanceMetric.DotProduct);
```

---

## VectorDistanceMetric

Enumeration of supported distance metrics.

**Signature**:
```csharp
public enum VectorDistanceMetric : byte
{
    Euclidean = 0,
    Cosine = 1,
    DotProduct = 2
}
```

**Metric Characteristics**:

### Cosine (Default)
- **Formula**: `1 - (a·b) / (|a| × |b|)`
- **Range**: [0, 2] (0 = identical, 2 = opposite)
- **Best for**: Normalized embeddings, direction similarity
- **Invariant to**: Vector magnitude
- **Use case**: Most ML embeddings (BERT, Sentence Transformers)

### Euclidean
- **Formula**: `√(Σ(a - b)²)`
- **Range**: [0, ∞)
- **Best for**: Absolute distance, spatial coordinates
- **Sensitive to**: Vector magnitude
- **Use case**: Image features, spatial data

### DotProduct
- **Formula**: `a·b`
- **Range**: (-∞, ∞)
- **Best for**: Magnitude-aware similarity
- **Sensitive to**: Both direction and magnitude
- **Use case**: Recommendation scores, unnormalized embeddings

---

## Error Handling

### Missing Plugin Registration

If VectorSearchPlugin is not initialized, operations throw:

```csharp
LiteException: Vector indexing requires the LiteDB.Vector extension package.
Install via: dotnet add package LiteDB.Vector
```

**Affected operations**:
- All EnsureIndex overloads
- WhereNear / TopKNear queries (when index exists)
- VECTOR_SIM expression evaluation

---

### Dimension Mismatch

If query vector dimensions don't match index:

```csharp
LiteException: Query vector dimensions (256) do not match index dimensions (384)
```

**Validation occurs**:
- During WhereNear / TopKNear calls
- Before executing vector similarity searches

---

### Invalid Vector Data

If document field is not a valid vector:

**Behavior**: Document is excluded from index (no exception)

**Invalid cases**:
- Field is null or missing
- Field is not BsonArray or BsonVector
- Array contains non-numeric values
- Array length != index dimensions

---

### Index Already Exists

If attempting to create index with conflicting configuration:

```csharp
LiteException: Vector index 'embedding_idx' already exists with different options.
```

**Resolution**: Drop existing index first, or use different name

---

## Thread Safety

All extension methods are thread-safe and respect LiteDB's transaction model:
- Index creation acquires write lock
- Queries acquire read lock
- Multiple concurrent reads are supported
- Writes are serialized by engine

---

## Performance Guidelines

### Index Creation
- **Time**: O(n log n) for n documents
- **Recommendation**: Create indexes before bulk inserts when possible
- **Progress**: No built-in progress reporting (process in batches if needed)

### Query Performance
- **WhereNear**: O(log n) average with index, O(n) without
- **TopKNear**: Similar to WhereNear but with automatic limit
- **Recommendation**: Always create indexes for production queries

### Memory Usage
- **Query overhead**: O(efSearch) ≈ O(32) candidate set
- **Index overhead**: ~330 bytes per document for inline vectors

---

## Compatibility

### Supported .NET Versions
- .NET Standard 2.0+ (netstandard2.0)
- .NET 8.0+ (net8.0)

### Database Compatibility
- LiteDB v6.x (requires compatible core version)
- File format: No changes (indexes from previous versions work)

### API Stability
- All public types and methods are stable
- Minor version bump if signatures change
- Major version bump if breaking changes
