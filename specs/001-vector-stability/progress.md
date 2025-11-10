# Progress Log - Vector Stability Hardening

## 2025-11-10 00:30
- Completed T001 by creating `specs/001-vector-stability/research.md` with regression summaries for dot-product normalization, legacy API removal, plugin scope/isolation, and artifact cleanup contexts.
- Completed T002 by creating `specs/001-vector-stability/quickstart.md`, outlining US1 fail-first steps plus the full dotnet/git verification loop to reuse during later phases.

## 2025-11-10 00:40
- Completed T003 by adding `LiteDB.Vector.Tests/Infrastructure/VectorTestContext.cs`, which seeds vector collections, normalizes expressions, configures metadata bags, and exposes helpers for `WhereNear`, `TopKNear`, and metadata-driven similarity queries; verified via `dotnet build LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj -c Debug`.

## 2025-11-10 00:44
- Completed T004 by extending `LiteDB.Tests/Utils/DatabaseFactory.cs` with `DatabaseFactoryOptions`, `LiteDatabaseGroup`, and `CreateMany`, enabling multi-instance LiteDatabase setups plus coordinated disposal; added `LiteDB.Tests/Utils/DatabaseFactoryTests.cs` to prove independent state and per-instance plugin wiring, confirmed with `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -f net8.0 --filter DatabaseFactoryTests`.

## 2025-11-10 00:51
- Completed T005 by adding `DotProductMaxDistanceRegression` to `LiteDB.Vector.Tests/VectorIndex_Tests.cs`, seeding vectors via `VectorTestContext` to assert both metadata-injected and LINQ `WhereNear` flows honor the same dot-product threshold before normalization fixes land.

## 2025-11-10 00:59
- Completed T006 by running `dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj -f net8.0 --filter FullyQualifiedName~DotProductMaxDistanceRegression`, confirming the failure (`Expected metadataResults to be equal to {1}, but {1, 2, 3} contains 2 item(s) too many.`) and documenting the snapshot in `specs/001-vector-stability/quickstart.md`.

## 2025-11-10 01:02
- Completed T007 by adding `VectorEnsure.NormalizeMaxDistance` in `LiteDB.Vector/Utils/VectorEnsure.cs`, centralizing the dot-product threshold negation logic so metadata, planner, and extension paths can share a single canonical normalization routine.

## 2025-11-10 01:07
- Completed T008 by teaching `LiteDB/Engine/Query/Query.cs` to version vector metadata bags at `VectorMetadataVersionNormalized = 2`, normalize max-distance writes via the new helper logic (including upgrades for legacy bags), and ensure `VectorMetric` updates re-normalize stored thresholds; validated via `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -f net8.0 --filter QueryMetadataBagTests`.

## 2025-11-10 01:14
- Completed T009 by propagating the shared normalization into the plugin layer: `VectorQueryMetadata` now declares `Version = 2`, `VectorSearchPlugin` registers that version, `QueryableExtensions`/`VectorIndexPlanningRule` consume `VectorEnsure.NormalizeMaxDistance`, and test helpers (`VectorTestContext`) store normalized thresholds. Regression test still fails as expected (`Expected metadataResults to be equal to {1}, but {1, 2, 3} contains 2 item(s) too many.`) via `dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj -f net8.0 --filter FullyQualifiedName~DotProductMaxDistanceRegression`.

## 2025-11-10 01:31
- Completed T010 by updating `VectorScoreQueryableResult` to interpret normalized thresholds (distance-based comparisons) and re-normalize legacy metadata reads; confirmed `VectorExtensions_Tests` still pass (`dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj -f net8.0 --filter FullyQualifiedName~VectorExtensions_Tests`) while the dot-product regression remains red for fail-first validation.

## 2025-11-10 01:32
- Completed T011 by extending `LiteDB.Tests/Plugins/QueryMetadataBagTests.cs` with a dot-product normalization upgrade test, ensuring legacy bags bump to version 2 and persist negated thresholds; validated via `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -f net8.0 --filter QueryMetadataBagTests`.

## 2025-11-10 01:45
- Completed T012 by deleting the obsolete `WhereNear`, `TopKNear`, and `FindNearest` members from `LiteDB/Client/Database/LiteQueryable.cs`, removing the last `VectorCompatibility` hooks from the core queryable surface so only the plugin extensions expose vector helpers.
- Completed T013 by sweeping the tree (`rg -l "WhereNear" -g "*.cs"`, `git ls-files "LiteDB.Tests/Client/*"`) to confirm every caller lives in plugin-aware projects and already imports `LiteDB.Vector`; no LiteQueryable-specific client tests remained, so nothing needed deleting.
- Completed T014 by updating `docs/plugins/plugin-development.md` and `LiteDB.Vector/README.md` with guidance that vector LINQ helpers now come exclusively from `LiteQueryableVectorExtensions`, making it clear that consumers must register `VectorSearchPlugin` and `using LiteDB.Vector;` to access `WhereNear`/`TopKNear`.

## 2025-11-10 01:50
- Completed T015 by replacing the static fallback registry in `LiteDB/Document/BsonType.cs` with a `ConditionalWeakTable` cache keyed by `ILitePluginContext`, ensuring each database owns an isolated BSON type registry while retaining a deterministic default context for legacy callers; the obsolete `ReplaceFallbackRegistry` no longer mutates global state. Verified with `dotnet build LiteDB/LiteDB.csproj -c Debug`.

## 2025-11-10 02:00
- Completed T016 by mirroring the per-context approach for page factories: `LiteDB/Engine/Pages/PageFactoryRegistry.cs` now caches registries via `ConditionalWeakTable` with a default plugin context, and `LiteDB/Client/Database/LiteDatabaseServices.cs` no longer mutates global state during construction. Confirmed with `dotnet build LiteDB/LiteDB.csproj -c Debug`.

## 2025-11-10 02:05
- Completed T017 by promoting the plugin-side registries to reusable classes (`LiteDB/Plugins/Bson/PluginBsonTypeRegistry.cs`, `LiteDB/Plugins/Storage/PluginPageFactoryRegistry.cs`) and updating `ILitePluginContext` contracts/imports so each database can instantiate its own thread-safe BSON/page factory registries; `DefaultPluginContext` now composes these exposed implementations. Verified via `dotnet build LiteDB/LiteDB.csproj -c Debug`.
