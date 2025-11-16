# Plugin API Contract

**Version**: 1.0  
**Package**: LiteDB.Vector  
**Namespace**: LiteDB.Vector

## Plugin Registration

### VectorSearchPlugin.Initialize

Registers vector search capabilities with a LiteDatabase instance. This method is invoked automatically by `LiteDatabase` when the plugin is supplied through the `plugins` constructor parameter; applications should not call it directly.

**Signature**:
```csharp
public void Initialize(LiteDatabase database, ILitePluginContext context)
```

**Parameters**:
- `database`: The LiteDatabase instance to extend
- `context`: Plugin context providing registration entry points

**Effects**:
- Registers the `VECTOR_DIST` binary operator for expression evaluation and orders precedence rules
- Registers the `VECTOR_SIM` alias (optional) that mirrors similarity semantics when supported by the metric
- Registers the `VECTOR_DIST`/`VECTOR_SIM` function forms with optional metric parameter
- Registers `VectorIndexStrategy` for index operations and planner integration
- Registers vector score projection services (`WithVectorScore`, `VectorMatch<T>`) so distances are surfaced without recomputation
- Reads optional `vector.metric` connection string parameter

**Exceptions**:
- `ArgumentNullException`: If database or context is null

**Example**:
```csharp
using var db = new LiteDatabase(
    "mydata.db",
    plugins: new[] { VectorSearchPlugin.Instance });

// Vector operations now available
```

**Connection String Options**:
- `vector.metric=cosine|euclidean|dotproduct` - Sets default metric for indexes and ad-hoc distance queries when a metric is not specified explicitly

---

## Index Strategy

### IIndexStrategy Implementation

The plugin provides a vector index strategy accessible via the plugin framework.

**Kind**: `"vector"`  
**IndexTypeCode**: `1`

### EnsureIndex

Creates or verifies a vector index on a collection field.

**Signature**:
```csharp
bool EnsureIndex(object snapshot, object collection, string name, 
                 BsonExpression expression, BsonDocument options)
```

**Parameters**:
- `snapshot`: Snapshot instance (opaque to external callers)
- `collection`: CollectionPage instance (opaque to external callers)
- `name`: Index name (unique within collection)
- `expression`: BsonExpression pointing to vector field
- `options`: Configuration document with required keys

**Options Document**:
```json
{
  "dimensions": 384,           // Required: ushort (1-65535)
  "metric": "cosine"          // Optional: "cosine"|"euclidean"|"dotproduct" or numeric 0|1|2
}
```

**Returns**:
- `true`: Index was created
- `false`: Index already exists with identical configuration

**Exceptions**:
- `LiteException`: If index exists with different configuration
- `LiteException`: If options are missing or invalid
- `ArgumentNullException`: If any parameter is null

**Side Effects**:
- Creates VectorIndexMetadata in collection page
- Builds HNSW graph for existing documents
- Allocates VectorIndexPage instances

---

### DropIndex

Removes a vector index from a collection.

**Signature**:
```csharp
bool DropIndex(object snapshot, object collection, string name)
```

**Parameters**:
- `snapshot`: Snapshot instance
- `collection`: CollectionPage instance
- `name`: Index name to drop

**Returns**:
- `true`: Index was dropped
- `false`: Index did not exist

**Side Effects**:
- Deletes all VectorIndexPage instances
- Removes VectorIndexMetadata from collection
- Frees allocated pages to free list

---

### OnDocumentUpsert

Maintains vector indexes when documents are inserted or updated.

**Signature**:
```csharp
void OnDocumentUpsert(object snapshot, object collection, 
                     object dataBlock, BsonDocument document)
```

**Parameters**:
- `snapshot`: Snapshot instance
- `collection`: CollectionPage instance  
- `dataBlock`: PageAddress of document's data block
- `document`: The document being upserted

**Behavior**:
- Evaluates index expression against document
- If vector value exists: Inserts/updates in graph
- If vector value missing/invalid: Removes from graph if present
- Updates all vector indexes on the collection

**Vector Validation**:
- Must be BsonArray or BsonVector type
- All elements must convert to float
- Length must match index dimensions

---

### OnDocumentDelete

Maintains vector indexes when documents are deleted.

**Signature**:
```csharp
void OnDocumentDelete(object snapshot, object collection, object dataBlock)
```

**Parameters**:
- `snapshot`: Snapshot instance
- `collection`: CollectionPage instance
- `dataBlock`: PageAddress of document being deleted

**Behavior**:
- Removes nodes from all vector indexes
- Updates neighbor lists to maintain graph connectivity
- Frees index pages if empty

---

## Expression Functions

### VECTOR_DIST Operator

Computes the configured vector distance between two vectors in query expressions. The optional `VECTOR_SIM` alias returns similarity when supported by the metric.

**Binary Operator Syntax**:
```sql
$.Embedding VECTOR_DIST @target
```

**Function Syntax**:
```sql
VECTOR_DIST($.Embedding, @target [, 'cosine'])
```

**Signature**:
```csharp
public static BsonValue VectorDistance(BsonValue left, BsonValue right, VectorDistanceMetric? metric = null)
```

**Parameters**:
- `left`: First vector (BsonArray or BsonVector)
- `right`: Second vector (BsonArray or BsonVector)
- `metric`: Optional metric override (defaults to index configuration or connection-string default)

**Returns**:
- `BsonValue`: Distance value (0 = identical for cosine, higher is farther)
- `BsonValue.Null`: If vectors invalid or dimension mismatch

**Distance Formula**:
```
cosine_distance = 1 - (dot(v1, v2) / (|v1| * |v2|))
```

**Examples**:
```csharp
// In query where clause
collection.Find(Query.Where("$.Embedding VECTOR_DIST @target < 0.5", target));

// In projection
collection.Find(Query.All("distance", 1), 
    "{ distance: VECTOR_DIST($.Embedding, @target) }", target);

// Similarity alias (cosine metrics only)
collection.Find(Query.All("similarity", 1),
    "{ similarity: VECTOR_SIM($.Embedding, @target) }", target);
```

**Error Handling**:
- Returns Null for non-vector types
- Returns Null for dimension mismatch
- Returns Null for NaN or non-numeric array elements
- Returns Null for zero-magnitude vectors
- Throws `LiteException(VectorErrors.MetricDoesNotSupportSimilarity)` when `VECTOR_SIM` is used with an unsupported metric

---
### VectorIndexQuery

Internal query plan node for vector distance searches.

**Constructor**:
```csharp
VectorIndexQuery(VectorIndexMetadata metadata, float[] targetVector, 
                 double maxDistance, int? limit)
```

**Behavior**:
- Executes nearest neighbor search via VectorIndexService
- Recomputes exact distances for candidate nodes and applies deterministic ordering with `_id` tie-breaking
- Applies `maxDistance` filtering and LIMIT/OFFSET semantics before returning results
- Falls back to a full scan when the index is unavailable or metric override is incompatible, while preserving the same filtering semantics

---

### VectorScoreProjection

Linq pipeline node that materializes distance (and optional similarity) scores without re-running the vector search.

**Constructor**:
```csharp
VectorScoreProjection(VectorIndexQuery source, VectorScoreKind kind)
```

**Behavior**:
- Attaches the computed score to each row as a `VectorMatch<T>` record
- Converts cosine distance to similarity when `kind == Similarity`; throws when the metric lacks a similarity transform
- Preserves the ordering emitted by `VectorIndexQuery`

---

## Thread Safety

All plugin operations respect LiteDB's transaction model:
- Index modifications require write lock
- Searches require read lock
- Operations within a transaction see consistent state
- No additional locking beyond LiteDB's engine

---

## Error Codes

| Code | Message | Cause |
|------|---------|-------|
| 0 | "Vector index options must include a numeric 'dimensions' value." | Missing or non-numeric dimensions in options |
| 0 | "Vector index options must include a 'metric' value when no default is configured." | Missing metric without connection string default |
| 0 | "Vector index 'metric' option must be numeric or one of 'euclidean', 'cosine', or 'dotproduct'." | Invalid metric value |
| 0 | "Vector index '{name}' already exists with different options." | Attempt to create index with conflicting config |
| VECTOR_DIMENSIONS_EXCEEDED | "Vector dimension limit (65535) exceeded for field '{field}'." | Attempt to index or query vectors beyond UInt16 capacity |
| VECTOR_METRIC_MISMATCH | "Vector query metric '{query}' does not match index metric '{index}' and cannot fall back to scan." | Metric override not compatible with available index |
| VECTOR_SIMILARITY_UNSUPPORTED | "`VECTOR_SIM` is not supported for metric '{metric}'." | Similarity alias used with unsupported metric |
| INDEX_ALREADY_EXIST | "Index '{name}' already exists." | Index exists with different type (non-vector) |

---

## Backward Compatibility

### Database Files
- Vector indexes created before migration remain compatible
- File format unchanged (page types, structures persist in core)
- No migration or rebuild required

### API Compatibility  
- All public methods retain identical signatures
- Extension method namespaces unchanged (LiteDB.Vector)
- VectorIndexOptions constructor unchanged
- Query syntax unchanged

### Breaking Changes
**None** - provided users:
1. Reference LiteDB.Vector package
2. Pass `VectorSearchPlugin.Instance` through the `LiteDatabase` constructor (`plugins` parameter)

Without supplying the plugin, vector index operations throw a clear error:
```
LiteException: Vector indexing requires the LiteDB.Vector extension package. 
Install via: dotnet add package LiteDB.Vector
```
