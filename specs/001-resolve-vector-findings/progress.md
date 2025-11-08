# Progress Log - Resolve Vector Findings

## 2025-11-02

- T001: Confirmed active branch by reading `.git/HEAD` (`ref: refs/heads/001-resolve-vector-findings`).
- T002: Ran `dotnet restore` and `dotnet build LiteDB.sln -c Release`; build succeeded with existing net461 compatibility warnings and nullable/context notices in LiteDB project.
- T003: Created `artifacts_temp/vector-followup/` staging directory to capture upgrade reports and telemetry exports.
- T004: Ensured `LiteDB/Plugins/` hosts `Query/`, `Bson/`, `Storage/`, and `Indexing/` subdirectories for upcoming extensibility contracts.
- T005: Added `LiteDB/Plugins/Query/IQueryMetadataAccessor.cs` defining descriptor registration APIs for plugin-managed query metadata.
- T006: Extended `LiteDB/Plugins/DefaultPluginContext.cs` with query metadata registration helpers backed by a thread-safe accessor implementation.
- T007: Added `LiteDB/Plugins/Bson/IBsonTypeRegistry.cs` and initialized the default plugin context with a BSON type registry for plugin registrations.
- T008: Drafted `LiteDB/Plugins/Storage/IPageFactoryRegistry.cs` and wired the default context with a registry for page factory descriptors.
- T009: Exposed query metadata, BSON type, and page factory registries through `ILitePluginContext` and `EnsureIndexContext` for plugin consumption.
- T010: Added `LiteDB/Plugins/Query/QueryMetadataBag.cs` providing versioned, typed metadata storage validating reserved keys for plugin-managed query state.
- T011: Refactored `LiteDB/Engine/Query/Query.cs` to store plugin metadata bags, route SQL rendering through bag lookups, and keep legacy vector accessors as obsolete shims.
- T012: Updated `LiteDB/Engine/Query/QueryOptimization.cs` and `LiteDB/Plugins/QueryPlanningContext.cs` so planning rules align registered descriptors, expose metadata helpers, and surface vector order consumption without relying on legacy fields.
- T013: Added metadata registration helpers to `LiteDB/Plugins/EnsureIndexContext.cs` ensuring interceptors can register and retrieve descriptors through the default plugin context.
- T014: Added `LiteDB/Document/Bson/BsonTypeRegistry.cs` providing plugin-aware lookups with legacy fallbacks for core types.
- T015: Extended `LiteDB/Document/BsonType.cs` with registry-backed resolution helpers and synced `LiteDatabaseServices` to refresh fallback metadata.
- T016: Routed BsonValue sizing and JsonWriter output through registry-aware helpers so vector serialization flows via the new BSON registry.

## 2025-11-03

- T017: Introduced engine-side `PageFactoryRegistry` with plugin-aware fallbacks and updated `BasePage`/`LiteDatabaseServices` so page creation and reads are routed through plugin descriptors when available.
- T018: Wired page factory resolver through FileReaderV8, rebuild orchestration, and snapshot flow-propagating plugin contexts so data readers and rebuild paths honor plugin-provided page implementations.
- T019: Added `LiteDB/Plugins/Indexing/VectorIndexStrategyDescriptor.cs` defining delegates, dependency metadata, and context wrappers for plugin-managed vector index strategies.
- T020: Expanded `EnsureIndexContext` and `ILitePluginContext` so plugins can register/query vector strategy descriptors via a dedicated registry wired through the default plugin context.
- T021: Updated collection and query client APIs to resolve vector work through the strategy registry, calling plugin delegates when present and surfacing compatibility shims when the plugin is absent.

- T022: Added QueryMetadataBag regression suite covering descriptor-backed bags, fallback construction, and vector metadata population when no plugin context exists.

- T023: Added VectorRegistry integration coverage and updated VectorSearchPlugin to register query metadata, page factory, and vector strategy descriptors across the new plugin registries.
- T023b: Added plugin-absent integration coverage validating vector index creation and query helpers emit the shared plugin-required error message, and updated `LiteEngine.EnsureVectorIndex` to reuse the compatibility guard for consistent guidance.
- T024: Moved VectorIndexServiceFactory into LiteDB.Vector/Engine/Services, removing the core implementation and keeping plugin registration intact.
- T025: Removed vector query helpers from LiteQueryable/LiteRepository, exposed public metadata APIs, and rebuilt plugin-side extensions (QueryableExtensions) to supply the runtime behavior.
## 2025-11-04

- T026/T027: Rebuilt vector runtime plumbing after migration by restoring free-list helpers inside LiteDB.Vector/Engine/VectorIndexService.cs (GetFreeVectorPage/AddOrRemoveFreeVectorList and friends) and ensuring VectorIndexPage, VectorIndexNode, and VectorIndexMetadata now wrap the plugin-owned serializer in LiteDB.Plugins.Indexing/VectorIndexMetadataSerializer.cs. Latest dotnet build LiteDB.Vector/LiteDB.Vector.csproj -c Release succeeds (warnings only for intentional [Obsolete] shims).
- T028: Centralised query metadata keys in LiteDB.Vector/Query/VectorQueryMetadata.cs and updated LiteDB.Vector/Extensions/QueryableExtensions.cs, LiteDB.Vector/Query/VectorIndexPlanningRule.cs, and LiteDB.Vector/Extensions/VectorScoreQueryableResult.cs to consume the metadata bag while keeping legacy Query.Vector* fallbacks behind CS0618 suppression. Vector scoring and planning now flow exclusively through the plugin registries.
- Investigated T029 removal of vector InternalsVisibleTo entries. Removing the attributes from LiteDB/Utils/Constants.cs breaks LiteDB.Vector because the plugin still relies on internal engine types (BasePage, PageAddress, Snapshot). Retained the attributes temporarily and added CS0618 suppressions across the plugin to clear existing warnings while we design a plugin-facing abstraction.
- T030: Ran `rg "Vector" LiteDB` (205 matches limited to plugin hookpoints) and refreshed `specs/001-vector-core-cleanup/SUMMARY.md` to confirm the core assembly only retains the documented safety shims with no unintended vector runtime components.

## 2025-11-05

- T029/T029b: Documented the temporary `InternalsVisibleTo` allowance in LiteDB/Utils/Constants.cs with explicit notes on the Snapshot/PageAddress/TransactionService/BasePage dependencies and logged the vector-plugin-abstractions follow-up in specs/001-vector-core-cleanup/SUMMARY.md. `dotnet build LiteDB.Vector/LiteDB.Vector.csproj -c Release` still surfaces the pre-existing nullable/AES/CA2200 warnings but no new issues.
- T031: Expanded LiteDB.Vector.Tests/VectorIndex_Tests.cs with rebuild coverage (VectorIndex_Survives_Rebuild_With_Plugin_PageFactory) and explicit index-name assertions, added an InspectCollection helper, and updated vector index setup to use named indexes so metadata probes hit the relocated runtime. Adjusted LiteDB/Engine/Pages/BasePage to infer plugin page types (VectorIndexPage) via enum parsing fallback, ensuring snapshot page creation succeeds under the plugin registry. `dotnet test LiteDB.Vector.Tests -f net8.0` now passes end-to-end, confirming relocated runtime behaviour parity.
- T032: Added `scripts/vector/MigrationHelpers.cs` exposing metadata inspection, rebuild orchestration, plugin validation, and backup helpers for the upgrade tooling, then verified the project compiles via `dotnet build LiteDB.sln -c Release`.

## 2025-11-06

- T033: Added `scripts/vector/Invoke-VectorUpgrade.ps1`, a manifest-driven PowerShell orchestrator that compiles `MigrationHelpers.cs` against the built LiteDB/LiteDB.Vector assemblies, resolves reference packs, runs helper or shell commands per step, supports filtering/skipping validations, and emits optional Markdown summaries for `artifacts_temp/vector-followup`. Sanity-checked the workflow via a dry run (step filter) to ensure helper compilation succeeds.
- T034: Authored `specs/001-resolve-vector-findings/migration/upgrade-manifest.json` capturing the ordered upgrade/validation plan (preflight capture, helper-driven relocation, solution + plugin tests, and post-migration scans) with detailed `verifies` notes and rollback guidance so scripts and docs share a single manifest definition.
- T035: Refreshed `specs/001-vector-core-cleanup/verification/build-validation.json` so build validation now covers the Release build plus a dry-run invocation of `scripts/vector/Invoke-VectorUpgrade.ps1`, capturing manifest/report expectations and storage of the generated artifacts under `artifacts_temp/vector-followup`.
- T036: Updated `LiteDB/Engine/Engine/Index.cs` so `LiteEngine.EnsureVectorIndex`/`DropIndex` emit structured diagnostics (captured in `LiteException.Data["VectorDiagnostics"]` and via `LOG`) when the vector plugin or registry is missing, including collection/index metadata and registered strategy lists; refreshed `PluginAbsentTests` to assert the new diagnostics.
- T037: Introduced `LiteDB.Vector/Utils/VectorTelemetry.cs` plus telemetry integration inside `VectorSearchPlugin` to warn via `ILitePluginContext.Logger` when required registries are unavailable or initialization fails, pointing operators to `scripts/vector/Invoke-VectorUpgrade.ps1` for remediation.
- T038: Followed the quickstart checklist (Release build, `Invoke-VectorUpgrade.ps1` against `artifacts_temp/vector-followup/quickstart.db`, targeted plugin-absent test, and telemetry probe) and archived the consolidated log at `artifacts_temp/vector-followup/upgrade-report.md`.
- T039: Updated `specs/001-vector-core-cleanup/SUMMARY.md` verification coverage and annotated `diagrams/component-dependencies.md` so every gap node is marked resolved with the upgrade/telemetry evidence.
- T041: Added `System.Threading.Tasks.Extensions` (4.5.4) to the netstandard2.0 target item group in `LiteDB/LiteDB.csproj`, ensuring ValueTask-backed plugin APIs have the required reference and matching plan.md dependency notes.
