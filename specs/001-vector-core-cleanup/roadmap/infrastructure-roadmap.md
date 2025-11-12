# Infrastructure Roadmap

This roadmap translates the four identified plugin infrastructure gaps into actionable upgrades with scope sizing, dependencies, and acceptance criteria. Owners default to the Core Engine and Vector Plugin teams unless otherwise noted.

## Scope Overview

| Gap | Scope | Primary Drivers | Key Dependencies | Notes |
|-----|-------|-----------------|------------------|-------|
| `gap-query-state` | M | Remove vector fields from `Query` and planner | Plugin metadata bag in `LiteDB.Plugins`, compatibility layer | Requires updates to query planning contexts and unit tests.
| `gap-bson-serialization` | M | Externalize `BsonType.Vector` registration | Query-state bag ready, serialization registry design | Touches ENUM values, JSON writer, binary serializer compatibility.
| `gap-storage-pipeline` | L | Relocate vector pages, metadata, rebuild hooks | Query-state bag (for planning integration), page factory design | Largest effort; introduces extensible page factory and metadata contracts.
| `gap-indexing-extensibility` | S | Replace `EnsureVectorIndex` with plugin strategies | Query metadata bag (for targets), interceptor enhancements | Builds on existing interceptor registry; minimal engine fallout expected.

## Acceptance Criteria

### `gap-query-state`

1. Query planning exposes a plugin-owned metadata bag or strongly typed accessor available during plan generation and execution.
2. Core `Query` class no longer contains vector-specific properties (`VectorField`, `VectorTarget`, `VectorMaxDistance`, `VectorMetric`).
3. Existing vector queries execute via the plugin without breaking non-plugin deployments (graceful error when plugin absent).
4. Inventory record `query-planning-core` removes `gap-query-state` from `requiredGapIds` after upgrade.

### `gap-bson-serialization`

1. Plugins can register BSON type codes and serialization handlers without modifying `BsonType` or `BsonValue` source files.
2. Legacy databases storing vector data remain readable and writable, with automated shims delegating to the plugin registry.
3. JSON export/import honors plugin serializers for registered vector types.
4. Inventory record `bson-serialization-surface` no longer lists `gap-bson-serialization` once migration is complete.

### `gap-storage-pipeline`

1. Engine exposes a page factory/metadata extension point that allows plugins to register custom index pages and metadata writers.
2. Vector index rebuild and file reader flows call into plugin-provided hooks without touching core vector types.
3. Existing vector indexes load and rebuild successfully after the plugin assumes ownership.
4. Inventory record `storage-engine-vector` removes `gap-storage-pipeline` after the upgrade is deployed.

### `gap-indexing-extensibility`

1. Plugins can register full index strategies (create, update, query planning callbacks) through an extended `IIndexInterceptorRegistry` or successor API.
2. Core `EnsureVectorIndex` APIs become thin shims that forward to plugin strategies, with clear errors when the plugin is missing.
3. Public API documentation references plugin-owned helpers instead of core vector methods.
4. Inventory record `public-api-surface` removes `gap-indexing-extensibility` once the plugin strategy system is in place.
