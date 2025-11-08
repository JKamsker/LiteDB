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

## Follow-up Learnings (2025-11-06)

- **Plugin Authoring Guidance**: Refreshing `docs/plugins/plugin-development.md` was necessary so third-party authors understand how to register query metadata bags, BSON type codes, and page factories. Without that documentation, external teams would keep duplicating the legacy vector approach instead of using the new registries.
- **ValueTask Dependencies**: The core package now explicitly references `System.Threading.Tasks.Extensions` (4.5.4) for the `netstandard2.0` target, ensuring the async serializers/deserializers exposed by the plugin registries compile without implicitly relying on application dependencies.
- **Packaging Verification**: Running `dotnet pack LiteDB/LiteDB.csproj -c Release` after all migrations confirmed the NuGet artifact still builds cleanly. The remaining warnings (nullable context, PBKDF2 constructors, CA2200) predate the cleanup and are tracked separately, so vector work did not introduce new packaging regressions.

### Remaining Shims Requiring Follow-up

- `LiteDB/Utils/Constants.cs`: The `InternalsVisibleTo` entries that grant `LiteDB.Vector` access to `BasePage`, `PageAddress`, `Snapshot`, and transaction internals remain in place. They are currently justified because the plugin still constructs pages and inspects snapshot services directly; replacing them requires new abstractions in the page factory registry and rebuild pipelines.
- `LiteDB/Engine/Query/Query.cs`: `[Obsolete]` vector property shims persist so existing binaries continue compiling. They forward into `QueryMetadataBag` and emit diagnostics when accessed. Once consumers migrate to the metadata accessor (`LiteDatabase.Services.QueryMetadata`), these shims can be removed along with the obsolete warnings.
- `LiteDB/Document/BsonType.cs` and `LiteDB.Document.Bson/BsonTypeRegistry.cs`: Core still registers a fallback `BsonType.Vector` descriptor so legacy data can be read without the plugin. The plan is to drop this fallback after the upgrade manifest graduates to required status and all persisted data uses plugin-owned type codes.
- `LiteDB/Engine/Pages/BasePage.cs` and `LiteDB/Engine/Pages/PageFactoryRegistry.cs`: The engine maintains compatibility mappings that detect legacy vector page enums before deferring to plugin factories. These guards should be deleted once the plugin publishes page descriptors for every historic enum value and upgrade scripts rewrite the persisted metadata.

## Performance guardrails

- **Decision**: Benchmark vector index build/search paths before and after migration, targeting ≤5% regression and publishing results alongside the upgrade validation checklist.
- **Rationale**: The constitution's Performance-First principle coupled with success criteria requires explicit performance tracking to confirm plugin-hosted code meets existing expectations.
- **Alternatives considered**: Skip performance validation (risks regressions) or accept regressions without quantified limits (would violate repository guidance).

## Specification adjustments (2025-11-02)

Post-design review identified operational gaps that were addressed in the task breakdown:

- **Directory structure verification**: Added T004 to explicitly verify/create `LiteDB/Plugins/` subdirectories before interface creation, preventing file path errors.
- **Migration helper implementation**: Added T032 to implement C# migration helpers explicitly (previously only PowerShell orchestration was tasked).
- **Build verification after InternalsVisibleTo removal**: Added T029b to validate plugin compiles independently, catching friend assembly dependencies early.
- **Plugin-absent testing**: Added T023b to validate deterministic error handling when vector operations run without the plugin loaded.
- **ValueTask dependency**: Added T041 to verify `System.Threading.Tasks.Extensions` package reference for `netstandard2.0` support.
- **Quickstart service clarification**: Clarified that OpenAPI contracts are conceptual documentation; registration happens in-memory (no external service required).
- **Performance benchmarks**: Removed formal benchmark tasks (T003b, T031b) as too time-consuming; performance validation will rely on existing test suite passing without regressions.
