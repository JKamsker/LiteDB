# Vector Core Cleanup – Final Inventory Summary

## Inventory Snapshot

- Vector references scanned via `rg "Vector" LiteDB` on 2025-11-02: **196** matches across **28** files (`artifacts_temp/vector-cleanup/raw-search-results.txt`, `affected-files.txt`).
- Components tracked in inventory: **5** (covering Public API, Query Planning, Serialization, Storage Engine, Service Infrastructure).
- Migration decision mix: **2** `MoveToPlugin`, **3** `NeedsInfrastructure`, **0** `RemainInCore`.
- Open infrastructure gaps: **4** total (**3 Critical**, **1 High**); all mapped to at least one component.

## Component Coverage

| Area | Component ID | Decision | Files Covered | Blocking Gaps | Key Notes |
|------|--------------|----------|---------------|---------------|-----------|
| PublicApi | `public-api-surface` | MoveToPlugin | LiteDB/Engine/ILiteEngine.cs; LiteDB/Engine/Engine/Index.cs; LiteDB/Client/Shared/SharedEngine.cs; LiteDB/Client/Database/Collections/Index.cs; LiteDB/Client/Database/LiteRepository.cs; LiteDB/Client/Database/LiteQueryable.cs | `gap-indexing-extensibility` (High) | Replace EnsureVectorIndex entry points with plugin strategies before relocation. |
| ServiceInfrastructure | `service-infrastructure-factory` | MoveToPlugin | LiteDB/Engine/Services/VectorIndexServiceFactory.cs; LiteDB/Utils/Constants.cs | – | Relocate factory into LiteDB.Vector and remove core InternalsVisibleTo usage. |
| QueryPlanning | `query-planning-core` | NeedsInfrastructure | LiteDB/Engine/Query/Query.cs; LiteDB/Engine/Query/QueryOptimization.cs; LiteDB/Plugins/QueryPlanningContext.cs; LiteDB/Document/Expression/Parser/BsonExpressionType.cs | `gap-query-state` (Critical) | Requires plugin-owned query metadata bag to drop vector-specific fields. |
| Serialization | `bson-serialization-surface` | NeedsInfrastructure | LiteDB/Document/BsonType.cs; LiteDB/Document/BsonValue.cs; LiteDB/Document/BsonVector.cs; LiteDB/Document/Json/JsonWriter.cs; LiteDB/Utils/Extensions/BufferSliceExtensions.cs; LiteDB/Engine/Disk/Serializer/BufferReader.cs; LiteDB/Engine/Disk/Serializer/BufferWriter.cs | `gap-bson-serialization` (Critical) | Needs plugin-managed BSON type registration before removal from core. |
| StorageEngine | `storage-engine-vector` | NeedsInfrastructure | LiteDB/Engine/Structures/VectorIndexNode.cs; LiteDB/Engine/Structures/VectorIndexMetadata.cs; LiteDB/Engine/Pages/VectorIndexPage.cs; LiteDB/Engine/Pages/BasePage.cs; LiteDB/Engine/Pages/CollectionPage.cs; LiteDB/Engine/FileReader/IndexInfo.cs; LiteDB/Engine/FileReader/FileReaderV8.cs; LiteDB/Engine/Services/SnapShot.cs; LiteDB/Engine/Engine/Rebuild.cs | `gap-storage-pipeline` (Critical) | Pending plugin-accessible page factory and metadata API. |

## Migration Decision Status

- `move-to-plugin-short` (owners: Vector Plugin Team) – covers Public API and Service Infrastructure scope; prerequisites focus on plugin extension methods, index interceptors, and factory relocation.
- `requires-infrastructure` (owners: Core Engine Team + Vector Plugin Team) – blocks Query Planning, Serialization, Storage Engine until new plugin extensibility hooks ship.
- `remain-in-core` – retained as contingency; no components rely on it after cleanup planning.

## Infrastructure Gap Outlook

| Gap ID | Category | Impact | Linked Components | Resolution Focus |
|--------|----------|--------|-------------------|------------------|
| `gap-indexing-extensibility` | Indexing | High | `public-api-surface` | Expand `IIndexInterceptorRegistry` to allow full plugin-owned index strategies and EnsureVectorIndex shims. |
| `gap-query-state` | QueryState | Critical | `query-planning-core` | Introduce plugin query-state bag to hold vector metadata outside the core query model. |
| `gap-bson-serialization` | BsonSerialization | Critical | `bson-serialization-surface` | Add plugin-managed BSON type registration to replace `BsonType.Vector` and related helpers. |
| `gap-storage-pipeline` | StoragePipeline | Critical | `storage-engine-vector` | Provide plugin hooks for page factories, metadata serialization, rebuild hooks, and snapshot participation. |

## Verification Coverage

- Build: `dotnet build LiteDB.sln -c Release` (`verification/verify-build-release`).
- Plugin Regression: `dotnet test LiteDB.Vector.Tests -c Release` (`verification/verify-plugin-compatibility`).
- Back-compat Databases: `dotnet test LiteDB.Tests -c Release --filter "Category=VectorBackCompat"` (`verification/verify-existing-database-compatibility`).
- Post-migration Search Sweep: `rg "Vector" LiteDB` (`verification/verify-search-post-migration`).

These steps remain aligned with the inventory artifacts and provide the acceptance harness once infrastructure work and plugin migrations proceed.
