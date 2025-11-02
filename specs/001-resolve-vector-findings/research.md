# Research Findings

## Query metadata bag

- **Decision**: Add a plugin-owned query metadata bag (`IQueryMetadataAccessor`) that replaces the vector-specific fields currently stored on `LiteDB.Engine.Query.Query` and expose it through the existing plugin contexts.
- **Rationale**: The cleanup inventory shows vector planning relies on bespoke properties (`VectorField`, `VectorTarget`, etc.). A metadata accessor keeps the core neutral while letting LiteDB.Vector attach strongly typed state during planning and execution.
- **Alternatives considered**: Continue storing vector fields on `Query` (keeps non-core data embedded in the core), or introduce per-plugin `Query` subclasses (would fragment engine code paths and complicate serialization).

## BSON type registration

- **Decision**: Provide a plugin-managed BSON type registry that reserves numeric codes, serializers, and deserializers while the core forwards legacy `BsonType.Vector` lookups into the registry until consumers migrate.
- **Rationale**: Vector serialization currently depends on enum values and helpers baked into `BsonType`/`BsonValue`. Delegating to a registry lets the plugin own serialization logic yet preserves compatibility for persisted data.
- **Alternatives considered**: Re-encode vectors as generic arrays (breaks LINQ helpers and stored data) or leave the enum in core permanently (violates Plugin-First principle and keeps vector debt in place).

## Storage page factory

- **Decision**: Introduce a page factory extension API that lets plugins register custom page constructors, metadata serializers, and rebuild callbacks for index pages such as `VectorIndexPage`.
- **Rationale**: Vector index structures cannot leave the core until plugins can request page instances, control persistence, and participate in rebuild/file-reader flows. The factory API isolates page creation while maintaining safety checks in the engine.
- **Alternatives considered**: Keep vector page types in `LiteDB.Engine.Pages` (blocks cleanup) or repurpose existing page types with plugin-defined payloads (would erode type safety and complicate recovery).

## Index strategy replacement

- **Decision**: Expand the index strategy registration so LiteDB.Vector provides the `EnsureVectorIndex` behavior, including command builders and planning callbacks, while the core hosts only compatibility shims that forward to plugin strategies.
- **Rationale**: The inventory highlights residual public APIs and factories tied to vector indexing. Unifying registration ensures no future friend assemblies are needed and keeps index behaviors contained within plugins.
- **Alternatives considered**: Preserve bespoke vector APIs in the core indefinitely (contradicts cleanup goals) or expose additional internal hooks (increases maintenance burden and weakens encapsulation).

## Migration & upgrade tooling

- **Decision**: Build scripted upgrade steps (PowerShell + C# helpers) that copy existing vector metadata into plugin-managed storage, run validation queries, and update documentation in `specs/001-vector-core-cleanup/verification`.
- **Rationale**: Success criteria demand a verified path from legacy databases to the plugin-backed implementation. Scripts plus documentation reduce human error and allow CI to enforce upgrade readiness.
- **Alternatives considered**: Rely solely on manual runbooks (error-prone) or postpone tooling until after code migration (risks shipping without a safe upgrade story).

## Observability & diagnostics

- **Decision**: Emit structured logs and health warnings when vectors are requested but the plugin is missing or incompatible, and surface guidance through quickstart documentation.
- **Rationale**: Edge cases call for deterministic failure modes. Observability helps operators identify configuration mistakes within one deployment cycle as demanded by the success criteria.
- **Alternatives considered**: Silent failures with fallback behavior (would hide misconfiguration) or hard crashes (unacceptable for production workloads).

## Performance guardrails

- **Decision**: Benchmark vector index build/search paths before and after migration, targeting ≤5% regression and publishing results alongside the upgrade validation checklist.
- **Rationale**: The constitution’s Performance-First principle coupled with success criteria requires explicit performance tracking to confirm plugin-hosted code meets existing expectations.
- **Alternatives considered**: Skip performance validation (risks regressions) or accept regressions without quantified limits (would violate repository guidance).
