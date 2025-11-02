# Data Model: Vector Plugin Migration

**Date**: 2025-11-02  
**Feature**: Vector Search Plugin Migration  
**Status**: Phase 1 Design

## Overview

This document defines the data model and relationships for the vector search plugin migration. The model separates core database structures (which remain in LiteDB) from plugin-specific implementations (which move to LiteDB.Vector).

## Core Domain (Remains in LiteDB)

### BsonVector
**Location**: `LiteDB/Document/BsonVector.cs`  
**Purpose**: Fundamental BSON data type for vector storage  
**Responsibility**: Serialization/deserialization of vector arrays

```csharp
public class BsonVector : BsonValue
{
    public float[] Values { get; }
    public int Length { get; }
    
    // Serialization methods
    public BsonVector(float[] values)
    public override BsonType Type => BsonType.Vector
}
```

**Relationships**:
- Stored in BsonDocument as a field value
- Converted to/from BsonArray for query operations
- Referenced by VectorIndexService for distance calculations

**Validation Rules**:
- Values array must not be null
- All float values must be valid (not NaN)
- Length must match index dimension requirements

---

### VectorIndexMetadata
**Location**: `LiteDB/Engine/Structures/VectorIndexMetadata.cs`  
**Purpose**: Persisted configuration for a vector index  
**Responsibility**: Store index settings in collection page

```csharp
internal class VectorIndexMetadata
{
    public byte Slot { get; }           // Index slot in collection (0-255)
    public ushort Dimensions { get; }   // Vector dimension count
    public VectorDistanceMetric Metric { get; } // Distance calculation method
    public PageAddress Root { get; set; }       // HNSW graph entry point
    public uint Reserved { get; set; }          // Future use
    
    // Persistence (14 bytes total)
    public VectorIndexMetadata(BufferReader reader)
    public void UpdateBuffer(BufferWriter writer)
    public static int GetLength() => 14
}
```

**Relationships**:
- One per vector index in a collection
- References VectorIndexNode tree via Root PageAddress
- Used by VectorIndexStrategy for index operations
- Persisted in CollectionPage metadata area

**State Transitions**:
- Created -> Active (when index built)
- Active -> Dropped (when index removed)

**Invariants**:
- Dimensions > 0
- Dimensions <= 65,535 (UInt16 limit enforced at creation and query time)
- Root may be Empty (no documents yet)
- Metric must be valid enum value

---

### VectorIndexNode
**Location**: `LiteDB/Engine/Structures/VectorIndexNode.cs`  
**Purpose**: HNSW graph node in the vector search structure  
**Responsibility**: Store vector and neighbor connections

```csharp
internal class VectorIndexNode
{
    public PageAddress Position { get; }    // Node's page location
    public PageAddress DataBlock { get; }   // Document's data block
    public byte LevelCount { get; }         // Graph levels (1-4)
    public int Dimensions { get; }          // Vector dimensions
    public bool HasInlineVector { get; }    // Vector storage mode
    public PageAddress ExternalVector { get; } // Overflow storage
    
    // Graph operations
    public IReadOnlyList<PageAddress> GetNeighbors(int level)
    public void SetNeighbors(int level, IReadOnlyList<PageAddress> neighbors)
    public bool TryAddNeighbor(int level, PageAddress address)
    public bool RemoveNeighbor(int level, PageAddress address)
    
    // Vector operations
    public float[] ReadVector()
    public void UpdateVector(float[] vector)
    
    // Constants
    public const int MaxLevels = 4
    public const int MaxNeighborsPerLevel = 8
}
```

**Relationships**:
- Stored in VectorIndexPage
- Connected to other nodes via neighbor lists (HNSW graph)
- References document via DataBlock PageAddress
- May reference external vector storage for large dimensions

**State Transitions**:
- Created -> Inserted (added to graph with neighbors)
- Inserted -> Deleted (removed from all neighbor lists)

**Invariants**:
- LevelCount ∈ [1, 4]
- Each level has ≤ 8 neighbors
- Vector dimensions match metadata dimensions
- DataBlock must be valid (not Empty)

---

### VectorIndexPage
**Location**: `LiteDB/Engine/Pages/VectorIndexPage.cs`  
**Purpose**: Database page containing VectorIndexNode instances  
**Responsibility**: Page-level node storage and retrieval

```csharp
internal class VectorIndexPage : BasePage
{
    public VectorIndexPage(PageBuffer buffer, uint pageID)
        : base(buffer, pageID, PageType.VectorIndex)
    
    // Node management
    public VectorIndexNode GetNode(byte index)
    public VectorIndexNode InsertNode(PageAddress dataBlock, float[] vector, 
                                      int bytesLength, byte levelCount, 
                                      PageAddress externalVector)
    public void DeleteNode(byte index)
    public IEnumerable<VectorIndexNode> GetNodes()
    
    // Free space management
    public static byte FreeListSlot(int freeBytes)
}
```

**Relationships**:
- Contains 0-255 VectorIndexNode instances
- Managed by core page system (PageService)
- Referenced by VectorIndexMetadata.Root
- Part of database file format

**Invariants**:
- PageType must be VectorIndex
- Node count ≤ 255 (byte index)
- Total page size ≤ PAGE_SIZE

---

## Plugin Domain (Moves to LiteDB.Vector)

### VectorSearchPlugin
**Location**: `LiteDB.Vector/VectorSearchPlugin.cs` (already there)  
**Purpose**: Plugin registration and initialization  
**Responsibility**: Wire vector capabilities into database

```csharp
public sealed class VectorSearchPlugin : ILitePlugin
{
    public static VectorSearchPlugin Instance { get; }
    
    public void Initialize(LiteDatabase database, ILitePluginContext context)
    {
        // Register distance operator + alias
        context.Expressions.RegisterBinaryOperator("VECTOR_DIST", ...)
        context.Expressions.RegisterFunction("VECTOR_DIST", ...)
        context.Expressions.RegisterFunction("VECTOR_SIM", ... /* similarity alias */)

        // Register vector index strategy and score projection
        var defaultMetric = TryReadDefaultMetric(context.ConnectionString["vector.metric"])
        context.Indexes.Register(new VectorIndexStrategy(context.Logger, defaultMetric))
        context.Query.RegisterScoreProjection(new VectorScoreProjectionFactory())

        // Provide connection-string activation (plugins=vector)
        context.Logger.Write(LogLevel.Debug, "VectorSearchPlugin enabled with default metric {0}", defaultMetric)
        
        context.Logger.Write(LogLevel.Information, "VectorSearchPlugin initialized.")
    }
}
```

**Relationships**:
- Implements ILitePlugin interface
- Creates VectorIndexStrategy instance
- Registers VectorExpressions functions and score projection hooks
- Singleton pattern for easy registration

**Lifecycle**:
- `LiteDatabase` calls `Initialize()` once per database instance when the plugin is supplied through the constructor
- Plugin remains active for database lifetime
- No cleanup required (stateless registration)

---

### VectorIndexStrategy
**Location**: `LiteDB.Vector/VectorIndexStrategy.cs` (already there)  
**Purpose**: IIndexStrategy implementation for vector indexes  
**Responsibility**: Coordinate vector index operations

```csharp
internal sealed class VectorIndexStrategy : IIndexStrategy
{
    public string Kind => "vector"
    public byte IndexTypeCode => 1
    
    // Index lifecycle
    public bool EnsureIndex(object snapshot, object collection, 
                           string name, BsonExpression expression, 
                           BsonDocument options)
    public bool DropIndex(object snapshot, object collection, string name)
    
    // Document lifecycle
    public void OnDocumentUpsert(object snapshot, object collection, 
                                object dataBlock, BsonDocument document)
    public void OnDocumentDelete(object snapshot, object collection, 
                                object dataBlock)
}
```

**Relationships**:
- Implements IIndexStrategy (plugin contract)
- Delegates to VectorIndexService for graph operations
- Interacts with Snapshot, CollectionPage (via object parameters)
- Manages VectorIndexMetadata instances

**Validation Rules**:
- options["dimensions"] must be numeric > 0
- options["metric"] must be valid VectorDistanceMetric
- expression must reference a valid field

---

### VectorIndexService
**Location**: `LiteDB/Engine/Services/VectorIndexService.cs` -> moves to `LiteDB.Vector/Engine/VectorIndexService.cs`  
**Purpose**: HNSW graph algorithm implementation  
**Responsibility**: Vector index CRUD and nearest neighbor search

```csharp
internal sealed class VectorIndexService
{
    // HNSW parameters (internal constants)
    private const int EfConstruction = 24
    private const int DefaultEfSearch = 32
    
    public VectorIndexService(Snapshot snapshot, Collation collation)
    
    // Index operations
    public void Insert(VectorIndexMetadata metadata, PageAddress dataBlock, float[] vector)
    public void Delete(VectorIndexMetadata metadata, PageAddress dataBlock)
    public void Upsert(CollectionIndex index, VectorIndexMetadata metadata, 
                      BsonDocument document, PageAddress dataBlock)
    
    // Search operations
    public IEnumerable<(BsonDocument Document, double Distance)> Search(
        VectorIndexMetadata metadata, float[] target, 
        double maxDistance, int? limit)
    
    // Graph maintenance
    private PageAddress GreedySearch(VectorIndexMetadata metadata, float[] target, 
                                     PageAddress entryPoint, int level, 
                                     Dictionary<PageAddress, float[]> vectorCache, 
                                     HashSet<PageAddress> visited)
    private List<NodeDistance> SearchLayer(VectorIndexMetadata metadata, 
                                          float[] target, PageAddress entryPoint, 
                                          int level, int efSearch, 
                                          Dictionary<PageAddress, float[]> vectorCache, 
                                          HashSet<PageAddress> visited)
    
    // Distance calculation
    private double CalculateDistance(VectorDistanceMetric metric, 
                                    float[] vector1, float[] vector2)
}
```

**Relationships**:
- Uses Snapshot for transaction consistency
- Reads/writes VectorIndexNode via VectorIndexPage
- Implements HNSW (Hierarchical Navigable Small World) algorithm
- Called by VectorIndexStrategy hooks

**Algorithm Details**:
- **Insert**: Add node, select level probabilistically, connect to nearest neighbors at each level
- **Search**: Multi-layer greedy search from entry point to level 0, beam search for candidates
- **Delete**: Remove node from all neighbor lists, update connections to maintain graph connectivity

**Performance Characteristics**:
- Insert: O(log n) average, O(n) worst case
- Search: O(log n) average with high recall
- Memory: O(n × MaxNeighbors × MaxLevels) for graph structure

---

### VectorIndexOptions
**Location**: `LiteDB/Client/Vector/VectorIndexOptions.cs` -> moves to `LiteDB.Vector/VectorIndexOptions.cs`  
**Purpose**: User-facing configuration for vector indexes  
**Responsibility**: Type-safe options for index creation

```csharp
public sealed class VectorIndexOptions
{
    public ushort Dimensions { get; }
    public VectorDistanceMetric Metric { get; }
    
    public VectorIndexOptions(ushort dimensions, 
                             VectorDistanceMetric metric = VectorDistanceMetric.Cosine)
}
```

**Relationships**:
- Used by extension methods (LiteCollectionVectorExtensions, etc.)
- Converted to BsonDocument for IIndexStrategy.EnsureIndex
- Validated by VectorIndexStrategy

**Validation Rules**:
- Dimensions > 0
- Metric must be valid enum value

---

### VectorDistanceMetric
**Location**: `LiteDB/Client/Vector/VectorDistanceMetric.cs` -> moves to `LiteDB.Vector/VectorDistanceMetric.cs`  
**Purpose**: Enumeration of supported distance metrics  
**Responsibility**: Type-safe metric selection

```csharp
public enum VectorDistanceMetric : byte
{
    Euclidean = 0,  // √(Σ(a-b)²) - L2 distance
    Cosine = 1,     // 1 - (a·b)/(|a||b|) - Angle-based
    DotProduct = 2  // a·b - Inner product
}
```

**Usage**:
- **Euclidean**: Absolute distance, sensitive to magnitude
- **Cosine**: Normalized similarity, ignores magnitude (default)
- **DotProduct**: Raw similarity, includes magnitude

**Relationships**:
- Used in VectorIndexMetadata (persisted)
- Used in VectorIndexOptions (user-facing)
- Used in VectorIndexService.CalculateDistance

---

### Extension Methods
**Location**: `LiteDB/Client/Vector/` -> moves to `LiteDB.Vector/Extensions/`  
**Purpose**: Fluent API for vector operations  
**Responsibility**: Convenience methods on collection/queryable

#### LiteCollectionVectorExtensions
```csharp
public static class LiteCollectionVectorExtensions
{
    public static bool EnsureIndex<T>(this ILiteCollection<T> collection, 
                                     string name, BsonExpression expression, 
                                     VectorIndexOptions options)
    public static bool EnsureIndex<T, K>(this ILiteCollection<T> collection, 
                                        Expression<Func<T, K>> keySelector, 
                                        VectorIndexOptions options)
}
```

#### LiteQueryableVectorExtensions
```csharp
public static class LiteQueryableVectorExtensions
{
    public static ILiteQueryable<T> WhereNear<T>(this ILiteQueryable<T> source, 
                                                 string vectorField, float[] target, 
                                                 double maxDistance)
    public static ILiteQueryableResult<T> TopKNear<T>(this ILiteQueryable<T> source, 
                                                      string field, float[] target, int k)
}
```

#### LiteRepositoryVectorExtensions
```csharp
public static class LiteRepositoryVectorExtensions
{
    public static bool EnsureIndex<T>(this ILiteRepository repository, 
                                     string name, BsonExpression expression, 
                                     VectorIndexOptions options, 
                                     string collectionName = null)
}
```

**Relationships**:
- Extend core interfaces (ILiteCollection, ILiteQueryable, ILiteRepository)
- Translate to BsonExpression and index operations
- Call collection.EnsureIndex with kind="vector"

---

### VectorExpressions
**Location**: `LiteDB.Vector/Expressions/VectorExpressions.cs` (already there)  
**Purpose**: Expression evaluator for vector distance and similarity operators  
**Responsibility**: Compute metric distance (and optional similarity) between vectors with validation and coercion rules

```csharp
internal static class VectorExpressions
{
    public static BsonValue VectorDistance(
        BsonValue left,
        BsonValue right,
        VectorDistanceMetric? metric = null);

    public static BsonValue VectorSimilarity(
        BsonValue left,
        BsonValue right,
        VectorDistanceMetric? metric = null);
}
```

**Implementation Notes**:
- Extracts vectors from `BsonArray`, `BsonVector`, or numeric literals; rejects invalid/coerced values with `BsonValue.Null`
- Delegates to `VectorMath` helpers for cosine/euclidean/dot product implementations
- `VectorSimilarity` maps to the cosine similarity result (`1 - distance`) and throws `VectorErrors.MetricDoesNotSupportSimilarity` when not supported
- Applies float32 coercion with overflow detection and rejects NaN/Infinity inputs

**Relationships**:
- Registered in `VectorSearchPlugin.Initialize`
- Powers both infix (`$.Embedding VECTOR_DIST @v`) and function (`VECTOR_DIST($.Embedding, @v, 'cosine')`) syntax
- Supplies scores to `VectorIndexQuery` and `VectorScoreProjection`

---

### VectorIndexQuery
**Location**: `LiteDB/Engine/Query/IndexQuery/VectorIndexQuery.cs` -> `LiteDB.Vector/Query/VectorIndexQuery.cs`  
**Purpose**: Query plan node for vector distance searches  
**Responsibility**: Execute nearest neighbor queries, re-score candidates, and expose deterministic scores

```csharp
internal sealed class VectorIndexQuery : IndexQuery
{
    private readonly VectorIndexMetadata _metadata;
    private readonly float[] _targetVector;
    private readonly double? _maxDistance;
    private readonly int? _limit;

    public override IEnumerable<VectorMatch<PageAddress>> Run(
        CollectionPage collection,
        IndexService indexer,
        CancellationToken token)
    {
        var service = new VectorIndexService(indexer);
        var candidates = service.Search(
            _metadata,
            _targetVector,
            _maxDistance,
            _limit,
            token);

        foreach (var candidate in ReorderAndFilter(candidates))
        {
            yield return candidate;
        }
    }

    private IEnumerable<VectorMatch<PageAddress>> ReorderAndFilter(
        IEnumerable<VectorCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            var distance = VectorMath.Distance(
                candidate.Vector,
                _targetVector,
                _metadata.Metric);

            if (_maxDistance.HasValue && distance > _maxDistance.Value)
            {
                continue;
            }

            yield return new VectorMatch<PageAddress>(
                candidate.DataBlock,
                distance,
                VectorMath.TrySimilarity(_metadata.Metric, distance));
        }
    }
}
```

**Relationships**:
- Extends `IndexQuery` base class
- Created by the planner for `WhereNear`, `TopKNear`, and `ORDER BY VECTOR_DIST` scenarios
- Relies on `VectorIndexService` for candidate generation and applies exact post-filtering + tie-breaking on (`distance`, `_id`)
- Provides input to `VectorScoreProjection` for score projection and to the data block loader for document materialization

---
### VectorScoreProjection
**Location**: `LiteDB.Vector/Query/VectorScoreProjection.cs` (new)  
**Purpose**: Wraps `VectorIndexQuery` results to attach score metadata without re-running the search  
**Responsibility**: Materialize `VectorMatch<T>` records honoring requested score kind

```csharp
internal sealed class VectorScoreProjection<T> : ILiteQueryEnumerable<VectorMatch<T>>
{
    private readonly ILiteQueryEnumerable<VectorMatch<T>> _source;
    private readonly VectorScoreKind _kind;

    public IEnumerator<VectorMatch<T>> GetEnumerator()
    {
        foreach (var match in _source)
        {
            yield return _kind == VectorScoreKind.Distance
                ? match
                : match with { Similarity = VectorMath.TrySimilarity(match.DistanceMetric, match.Distance) };
        }
    }
}
```

**Relationships**:
- Consumed by `.WithVectorScore` LINQ extension methods
- Ensures similarity projection only executes when supported; otherwise throws `VectorErrors.MetricDoesNotSupportSimilarity`
- Preserves ordering emitted by `VectorIndexQuery`

---

### VectorMatch<T>
**Location**: `LiteDB.Vector/Query/VectorMatch.cs` (new)  
**Purpose**: Immutable record linking a document (or page address) with vector scores  
**Responsibility**: Provide distance and similarity values to callers

```csharp
public readonly record struct VectorMatch<T>(
    T Document,
    double Distance,
    double? Similarity,
    VectorDistanceMetric DistanceMetric);
```

**Relationships**:
- Emitted by `VectorIndexQuery`, enriched by `VectorScoreProjection`, and consumed by `.WithVectorScore`
- Stored as a value type to minimize allocations during high-volume queries

---
## Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         CORE DOMAIN                              │
│                     (Remains in LiteDB)                          │
└─────────────────────────────────────────────────────────────────┘

┌─────────────┐      1     ┌──────────────────┐
│ BsonDocument│◄───────────│  BsonVector      │
│             │  contains  │                  │
│ - Id        │            │ - Values: float[]│
│ - Fields    │            │ - Length: int    │
└─────────────┘            └──────────────────┘
       │
       │ indexed by
       │
       ▼
┌──────────────────────┐      1      ┌────────────────────┐
│ VectorIndexMetadata  │─────────────│  VectorIndexNode   │
│                      │    Root     │                    │
│ - Slot: byte         │────────────►│ - Position         │
│ - Dimensions: ushort │             │ - DataBlock        │
│ - Metric: enum       │             │ - LevelCount: 1-4  │
│ - Root: PageAddress  │             │ - Neighbors[level] │
└──────────────────────┘             └────────────────────┘
       │                                      │
       │ stored in                            │ stored in
       │                                      │
       ▼                                      ▼
┌────────────────┐                  ┌──────────────────┐
│ CollectionPage │                  │ VectorIndexPage  │
│                │                  │                  │
│ - Metadata[]   │                  │ - Nodes[0-255]   │
└────────────────┘                  └──────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                        PLUGIN DOMAIN                             │
│                    (Moves to LiteDB.Vector)                      │
└─────────────────────────────────────────────────────────────────┘

┌──────────────────────┐      initializes     ┌──────────────────────┐
│ VectorSearchPlugin   │─────────────────────►│ VectorIndexStrategy  │
│                      │                      │                      │
│ + Initialize()       │                      │ + EnsureIndex()      │
│ (singleton)          │                      │ + DropIndex()        │
└──────────────────────┘                      │ + OnDocumentUpsert() │
       │                                      │ + OnDocumentDelete() │
       │ registers                            └──────────────────────┘
       │                                               │
       ▼                                              │ delegates to
┌──────────────────────┐                              │
│ VectorExpressions    │                              ▼
│                      │                      ┌──────────────────────┐
│ + VectorSimilarity() │                      │ VectorIndexService   │
│ (VECTOR_SIM)         │                      │                      │
└──────────────────────┘                      │ + Insert()           │
                                              │ + Delete()           │
                                              │ + Search()           │
┌──────────────────────┐                      │ (HNSW algorithm)     │
│ Extension Methods    │                      └──────────────────────┘
│                      │                               │
│ Collection.          │                              │ uses
│   EnsureIndex()      │                              │
│ Queryable.           │                              ▼
│   WhereNear()        │                      ┌──────────────────────┐
│   TopKNear()         │                      │ VectorIndexQuery     │
│ Repository.          │                      │                      │
│   EnsureIndex()      │                      │ + Run()              │
└──────────────────────┘                      └──────────────────────┘
       │                                               │
       │ uses                                         │ executes
       │                                              │
       ▼                                              ▼
┌──────────────────────┐                      ┌──────────────────────┐
│ VectorIndexOptions   │                      │ IndexNode results    │
│                      │                      │                      │
│ - Dimensions         │                      │ - DataBlock          │
│ - Metric             │                      │ - Distance (score)   │
└──────────────────────┘                      └──────────────────────┘
       │
       │ contains
       ▼
┌──────────────────────┐
│ VectorDistanceMetric │
│                      │
│ - Euclidean = 0      │
│ - Cosine = 1         │
│ - DotProduct = 2     │
└──────────────────────┘
```

## Migration Impact

### Core Library Changes
**What stays**:
- All page types and structures (VectorIndexMetadata, VectorIndexNode, VectorIndexPage)
- BsonVector data type
- PageType.VectorIndex enumeration value
- Plugin infrastructure (ILitePlugin, IIndexStrategy, etc.)

**What is removed**:
- `Engine/Services/VectorIndexService.cs` (moves to plugin)
- `Engine/Query/IndexQuery/VectorIndexQuery.cs` (moves to plugin)
- `Client/Vector/*` extension methods (move to plugin)

### Plugin Package Structure
```
LiteDB.Vector/
├── VectorSearchPlugin.cs              (exists)
├── VectorIndexStrategy.cs             (exists)
├── Engine/
│   └── VectorIndexService.cs          (moved from core)
├── Query/
│   └── VectorIndexQuery.cs            (moved from core)
├── Extensions/
│   ├── LiteCollectionVectorExtensions.cs   (moved from core)
│   ├── LiteQueryableVectorExtensions.cs    (moved from core)
│   └── LiteRepositoryVectorExtensions.cs   (moved from core)
├── Expressions/
│   └── VectorExpressions.cs           (exists)
└── VectorIndexOptions.cs              (moved from core)
    VectorDistanceMetric.cs            (moved from core)
```

### Breaking Changes
None for existing code that:
1. References LiteDB.Vector package
2. Passes `VectorSearchPlugin.Instance` to the `LiteDatabase` constructor (`plugins` parameter)

All public APIs remain identical.

## Validation Rules Summary

### Vector Data
- Dimensions must be > 0 and match index configuration
- All float values must be valid (not NaN, not Infinity)
- Vector length must be consistent within an index

### Index Configuration
- Dimensions ∈ [1, 65535] (ushort range)
- Metric must be one of: Euclidean, Cosine, DotProduct
- Index name must be unique within collection
- Expression must reference an existing field

### Graph Structure
- Level count ∈ [1, 4]
- Neighbors per level ≤ 8
- Root node must exist for non-empty index
- All PageAddress references must be valid

### Query Parameters
- Target vector dimensions must match index dimensions
- maxDistance must be ≥ 0
- limit must be > 0 if specified
- Vector field must have a vector index

## Performance Considerations

### Index Creation
- Time: O(n × log n) for n documents
- Space: O(n × MaxNeighbors × MaxLevels) ≈ O(n × 32) PageAddress references

### Nearest Neighbor Search
- Time: O(log n) average case with efSearch=32
- Space: O(efSearch) for candidate set
- Quality: High recall (>95%) for reasonable efSearch values

### Document Operations
- Insert: O(log n) graph insertion
- Update: O(log n) delete + O(log n) insert
- Delete: O(MaxNeighbors × MaxLevels) to remove from neighbor lists

### Memory Usage
- Index metadata: 14 bytes per index
- Node overhead: ~330 bytes per node (inline vector up to ~50 dimensions)
- External vectors: Additional page allocations for large dimensions
