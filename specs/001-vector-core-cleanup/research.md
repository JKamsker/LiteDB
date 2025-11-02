# Research Findings

## Inventory validation

- **Decision**: Adopt the existing inventory groupings (public APIs, query planning, BSON/serialization, storage metadata, service factories) as authoritative for `Vector` references under `LiteDB/`.
- **Rationale**: Running `rg "Vector" LiteDB` returned 196 matches, all of which map into the five areas already enumerated in the feature spec; no extra domains surfaced outside those groupings.
- **Alternatives considered**: Maintain a flat per-file list (too noisy for planning); defer inventory verification to later phases (risks missing debt before migration work begins).

## Performance budget

- **Decision**: Treat the plugin migration as having a ≤2% allowable regression budget for vector index creation and search throughput compared to the current core implementation.
- **Rationale**: LiteDB’s constitution mandates performance-first development; setting a tight performance envelope forces parity measurements when vector code leaves the core and discourages plugin-induced latency.
- **Alternatives considered**: Allow a broader (≥10%) regression window (would weaken performance guarantees); postpone performance targets until after migration (risks shipping slower vector search by default).

## Query metadata extensibility

- **Decision**: Extend the plugin system with a query-state bag that lets plugins attach and consume metadata during planning/execution, enabling removal of `VectorField`, `VectorTarget`, `VectorMaxDistance`, and `VectorMetric` from `LiteDB.Engine.Query.Query`.
- **Rationale**: Inspection of `ILitePluginContext` and `QueryPlanningContext` shows no API for plugin-owned query state today; a shared property bag or strongly-typed accessor keeps the core agnostic while letting vector rules manage their parameters.
- **Alternatives considered**: Keep vector-specific properties in `Query` (locks non-core logic in the library); subclass `Query` per plugin (would fragment engine code and break existing call sites).

## BSON type migration path

- **Decision**: Introduce a plugin-managed BSON type registration mechanism that lets `LiteDB.Vector` own the `Vector` representation while the core retains legacy deserialization shims for backward compatibility.
- **Rationale**: `BsonType.Vector`, `BsonValue.AsVector`, and JSON/serializer helpers live in core today; pulling them out requires a registry so plugins can reserve type codes and serializers without modifying `BsonType` or `BsonValue`.
- **Alternatives considered**: Re-encode vectors as plain arrays (breaks persisted databases and LINQ helpers); leave `BsonType.Vector` permanently in core (contradicts the cleanup objective and Principle VI).

## Storage page extensibility

- **Decision**: Design a plugin-accessible page factory/metadata extension API so `VectorIndexPage`, `VectorIndexMetadata`, and related rebuild/file-reader hooks can move to `LiteDB.Vector`.
- **Rationale**: The current engine hardcodes vector page types (`LiteDB.Engine.Pages.VectorIndexPage`, rebuild routines, snapshot handling). Without a registration mechanism the plugin cannot supply custom page layouts or participate in rebuild pipelines.
- **Alternatives considered**: Keep vector page types in core (fails Plugin-First principle); retrofit vector metadata into generic pages (would complicate base page layouts and hurt maintainability).

## Index entry point strategy

- **Decision**: Lean on `IIndexInterceptorRegistry` and a new plugin-owned index strategy to relocate `EnsureVectorIndex` behaviour from `LiteDB.Engine`/`LiteQueryable` into `LiteDB.Vector`.
- **Rationale**: Existing plugin APIs already let interceptors short-circuit `EnsureIndex` and register custom strategies; combining both ensures migrations can happen without widening `InternalsVisibleTo` usage in the core.
- **Alternatives considered**: Keep bespoke `EnsureVectorIndex` overloads in the public API (prevents plugin encapsulation); add more friend assemblies (contrary to minimizing surface area and keeping boundaries clear).

