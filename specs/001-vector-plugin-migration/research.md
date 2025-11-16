# Research: Vector Plugin Migration

**Date**: 2025-11-02  
**Feature**: Vector Search Plugin Migration  
**Status**: Phase 0 Complete

## Executive Summary

This document consolidates research findings for migrating vector search functionality from the LiteDB core library into the LiteDB.Vector extension package. The research validates that the plugin infrastructure is mature enough to support this migration and identifies all components that need to be moved.

## Current Architecture Analysis

### Existing Plugin Infrastructure

**Decision**: Use the existing IIndexStrategy plugin mechanism for vector indexes  
**Rationale**:
- The plugin infrastructure under `LiteDB/Plugins/` is already implemented and functional
- `IIndexStrategy` interface provides all necessary hooks: `EnsureIndex`, `DropIndex`, `OnDocumentUpsert`, `OnDocumentDelete`
- The `VectorSearchPlugin` class already exists in `LiteDB.Vector/` and uses this infrastructure correctly
- Engine integration points are already in place via `_plugins?.Indexes?.All` in Insert, Update, Delete, Rebuild operations

**Alternatives considered**:
- Creating a new plugin type specifically for vector indexes - rejected because IIndexStrategy already provides all needed capabilities
- Keeping vector code in core with optional compilation - rejected because constitution mandates plugin-first for non-core features

### Vector Code Inventory

#### Code Already in LiteDB.Vector Package
Located in `c:\Users\Jonas\repos\private\JKamsker\LiteDB\LiteDB.Vector\`:
- ✅ `VectorSearchPlugin.cs` - Plugin registration and initialization
- ✅ `VectorIndexStrategy.cs` - IIndexStrategy implementation
- ✅ `Expressions/VectorExpressions.cs` - Cosine similarity operator implementation

#### Code Currently in Core That MUST Be Moved
Located in `c:\Users\Jonas\repos\private\JKamsker\LiteDB\LiteDB\`:

**Engine Services** (needs to move):
- `Engine/Services/VectorIndexService.cs` - Complete HNSW graph implementation (979 lines)
  - Graph construction and search algorithms
  - Node insertion, deletion, and neighbor management
  - Distance calculation logic for all metrics

**Engine Query** (needs to move):
- `Engine/Query/IndexQuery/VectorIndexQuery.cs` - Query planning for vector similarity searches

**Client Extensions** (needs to move):
- `Client/Vector/LiteCollectionVectorExtensions.cs` - Collection-level vector operations
- `Client/Vector/LiteQueryableVectorExtensions.cs` - LINQ-style vector query extensions
- `Client/Vector/LiteRepositoryVectorExtensions.cs` - Repository-level vector operations
- `Client/Vector/VectorIndexOptions.cs` - Index configuration class
- `Client/Vector/VectorDistanceMetric.cs` - Distance metric enum

#### Code That MUST REMAIN in Core
These are fundamental database structures and cannot be moved:

**Core Data Structures** (stays in core - part of database format):
- `Engine/Structures/VectorIndexMetadata.cs` - Persisted metadata structure (14 bytes per index)
- `Engine/Structures/VectorIndexNode.cs` - HNSW graph node structure
- `Engine/Pages/VectorIndexPage.cs` - Page type for vector index storage
- `Document/BsonVector.cs` - Vector data type support

**Rationale for keeping in core**:
- These define the database file format and page types
- Removing them would break backward compatibility with existing databases
- They are referenced by the engine's page management system
- PageType.VectorIndex enum value is part of core page type enumeration

### Plugin Registration Pattern

**Decision**: Follow the spatial plugin pattern for registration  
**Rationale**:
- Spatial plugins (LiteDB.Spatial.*, currently being migrated) provide a proven pattern
- Users supply `VectorSearchPlugin.Instance` via the `LiteDatabase` constructor (`plugins` parameter), matching the spatial plugin pattern
- Connection string still supports default metric configuration: `vector.metric=cosine`
- Keeps registration explicit while avoiding repeated metric arguments

**Best Practice Pattern**:
```csharp
var connectionString = "mydata.db;vector.metric=cosine";
using var db = new LiteDatabase(
    connectionString,
    plugins: new[] { VectorSearchPlugin.Instance });
```

### Distance Metric Implementation

**Decision**: Keep all three distance metrics (Cosine, Euclidean, DotProduct) with Cosine as default  
**Rationale**:
- Different use cases require different metrics:
  - **Cosine**: Best for normalized embeddings (most ML models)
  - **Euclidean**: Best for absolute distance comparisons
  - **DotProduct**: Best for unnormalized magnitude-based ranking
- All metrics are already implemented in VectorIndexService
- Configurable per-index via VectorIndexOptions
- Connection string default avoids repetition: `vector.metric=cosine`

**Implementation locations**:
- Enum definition: `VectorDistanceMetric` (needs to move to LiteDB.Vector)
- Distance calculation: `VectorIndexService.CalculateDistance()` method
- Configuration: `VectorIndexOptions.Metric` property

### HNSW Algorithm Configuration

**Decision**: Keep current graph parameters (EfConstruction=24, DefaultEfSearch=32, MaxLevels=4, MaxNeighbors=8)  
**Rationale**:
- These values are tested and proven in production
- Changing them affects database format (node structure sizes)
- Making them configurable requires schema versioning
- Parameters are internal implementation details, not user-facing APIs

**Future enhancement**: Could expose `efSearch` as query-time parameter for quality/speed tradeoff

### Testing Strategy

**Decision**: All existing tests move with the code to LiteDB.Vector package  
**Rationale**:
- Tests in `LiteDB.Tests/Query/VectorIndex_Tests.cs` (1013 lines) are comprehensive
- Tests cover all user scenarios: create, query, update, delete, metrics
- Some tests use reflection to inspect internal structures - these may need adjustment
- Core structure tests should remain in core tests (BsonVector_Tests.cs)

**Test organization**:
```
LiteDB.Vector.Tests/           # New test project
├── VectorIndex_Tests.cs       # Moved from LiteDB.Tests
├── VectorExtensions_Tests.cs  # Moved from LiteDB.Tests
└── VectorPlugin_Tests.cs      # New plugin-specific tests

LiteDB.Tests/                  # Existing core tests
├── BsonValue/BsonVector_Tests.cs  # Remains (core data type)
└── Engine/Structures/          # Tests for metadata/node structures
```

### Backward Compatibility

**Decision**: Maintain full backward compatibility with existing databases and APIs  
**Rationale**:
- Database file format does not change (page types, structures remain in core)
- Public APIs remain identical (extension methods keep same signatures)
- Only change: users must reference LiteDB.Vector package and pass the plugin via the `LiteDatabase` constructor
- Existing databases with vector indexes will work after enabling the plugin via the constructor

**Migration path for users**:
1. Add NuGet package: `dotnet add package LiteDB.Vector`
2. Create the database with the plugin: `new LiteDatabase(connectionString, plugins: new[] { VectorSearchPlugin.Instance })`
3. No code changes to index creation or query code
4. Existing database files work without modification

### Error Handling Strategy

**Decision**: Provide clear, actionable error messages when vector extension is not installed  
**Rationale**:
- FR-010 requires clear guidance when extension is missing
- Engine's Index.cs already checks `_plugins?.Indexes?.GetByKind("vector")`
- When null, throw `LiteException` with message: "Vector indexing requires the LiteDB.Vector extension package. Install via: dotnet add package LiteDB.Vector"

**Implementation locations**:
- Index creation: `LiteEngine.EnsureIndex()` when kind="vector"
- Query execution: When vector similarity operator is used without registration
- Document operations: When vector index exists but strategy not registered

### Performance Baseline

**Decision**: Maintain current performance characteristics as baseline  
**Rationale**:
- Migration should not affect performance (same algorithms, just different assembly)
- Existing benchmarks in `LiteDB.Benchmarks/Benchmarks/Queries/QueryWithVectorSimilarity.cs` serve as baseline
- FR-003 requires ≤5% performance variance
- BenchmarkDotNet provides accurate measurement

**Benchmark coverage**:
- Index creation time for N documents
- Nearest neighbor search (k=1, 10, 100)
- Document insert/update/delete with vector indexes
- Memory usage for graph structure

## Technology Decisions

### Language & Framework
- **C#** targeting .NET Standard 2.0 and .NET 8.0 (as per repository constitution)
- Nullable reference types enabled in LiteDB.Vector project
- Unsafe code allowed where necessary (already used in VectorIndexNode)

### Dependencies
- **LiteDB core** (project reference) - provides plugin infrastructure and core types
- **No external dependencies** - keeps extension lightweight
- Test dependencies: xUnit, FluentAssertions (consistent with core)

### Build & Packaging
- Separate NuGet package: `LiteDB.Vector`
- Version synchronized with LiteDB core using MinVer
- XML documentation generation enabled
- Same target frameworks as core (netstandard2.0, net8.0)

## Integration Points

### Plugin System Integration
The plugin context provides these registries:
- **IIndexRegistry** - Register VectorIndexStrategy (already implemented)
- **IExpressionRegistry** - Register VECTOR_SIM operator (already implemented)
- **IQueryPlannerRegistry** - Not currently used by vector plugin
- **ILinqResolverRegistry** - Not currently used by vector plugin

### Engine Integration Points
Vector operations integrate via:
- **Index lifecycle**: EnsureIndex, DropIndex via IIndexStrategy
- **Document operations**: OnDocumentUpsert, OnDocumentDelete hooks
- **Query planning**: VectorIndexQuery for similarity searches
- **Page management**: VectorIndexPage handled by core page manager

### Data Type Integration
- **BsonVector** remains in core as fundamental data type
- Vector arrays use BsonType.Array or BsonType.Vector
- Conversion between BsonValue and float[] handled in both core and extension

## Migration Risks & Mitigations

### Risk: Breaking Changes for Existing Users
**Mitigation**: 
- All public APIs maintain identical signatures
- Extension methods move but keep same namespace (LiteDB.Vector)
- Clear upgrade documentation with step-by-step instructions
- Version bump follows semantic versioning (MAJOR bump for breaking change)

### Risk: Test Coverage Loss
**Mitigation**:
- Move all tests with the code
- Add new tests for plugin-specific behavior
- CI runs both core and extension tests
- Integration tests verify end-to-end scenarios

### Risk: Performance Regression
**Mitigation**:
- Run benchmarks before and after migration
- Document any performance changes (target: ≤5% variance)
- Cross-assembly call overhead is negligible for algorithm-heavy code
- Plugin registration happens once at startup

### Risk: Incomplete Code Migration
**Mitigation**:
- This research document identifies all code to move
- Grep searches verify no vector-specific code remains in core
- Compiler errors will catch missing references
- Test suite validates completeness

## Open Questions & Decisions

### Q: Should BsonVector remain in core or move to extension?
**Decision**: Remains in core  
**Rationale**: It's a fundamental BSON data type, used for serialization, not search-specific

### Q: Should VectorIndexMetadata and VectorIndexNode move to extension?
**Decision**: Remain in core  
**Rationale**: They define database file format and are part of page structures

### Q: Should we version the plugin API for future changes?
**Decision**: Not in initial migration  
**Rationale**: Constitution allows plugin framework evolution during initial phase; version when stabilized

### Q: How do we handle databases created before migration?
**Decision**: Full backward compatibility  
**Rationale**: File format unchanged, just requires plugin registration on open

### Q: Should VectorIndexService be public or internal?
**Decision**: Remains internal  
**Rationale**: Implementation detail, users interact via IIndexStrategy and extension methods

## Next Steps (Phase 1)

Based on this research, Phase 1 (Design & Contracts) should:
1. Define data model showing relationships between VectorIndexStrategy, VectorIndexService, and core structures
2. Document API contracts for extension methods that will move
3. Create migration checklist with all files to move
4. Generate quickstart documentation for users
5. Update agent context with new technology stack
6. Re-verify Constitution Check after design decisions

## References

- LiteDB Constitution v1.2.0 (Principle VI: Plugin-First for Non-Core Features)
- Feature Specification: `specs/001-vector-plugin-migration/spec.md`
- Existing Plugin Infrastructure: `LiteDB/Plugins/ILitePlugin.cs`
- Vector Plugin Implementation: `LiteDB.Vector/VectorSearchPlugin.cs`
- Spatial Plugin Migration (parallel effort): Constitution transition note
