# Progress Log - Vector Plugin Migration

## 2025-11-02

- Verified existing LiteDB.Vector assets for T001-T004, T007, and T008: `LiteDB.Vector.csproj` already targets `netstandard2.0;net8.0` with XML docs and nullable enabled, `VectorSearchPlugin` implements `ILitePlugin`, `VectorIndexStrategy` implements `IIndexStrategy`, and `Expressions/VectorExpressions.cs` is in place.
- Created the required folder structure under `LiteDB.Vector` (`Engine`, `Query`, `Extensions`) to prepare for upcoming code moves (T005).
- Scaffolded the new `LiteDB.Vector.Tests` xUnit project with FluentAssertions, Microsoft.NET.Test.Sdk, and a project reference to `LiteDB.Vector` to satisfy T006.
- Confirmed plugin extension contracts for T009-T010: `LiteDB/Plugins/ILitePlugin.cs` exposes `IIndexStrategy` with Ensure/Drop/Upsert/Delete hooks and `ILitePluginContext` dependencies, while `DefaultPluginContext` wires all registries and default services.
- Inspected retained core structures for T011 ensuring `LiteDB/Document/BsonVector.cs`, `LiteDB/Engine/Structures/VectorIndexMetadata.cs`, `VectorIndexNode.cs`, and `LiteDB/Engine/Pages/VectorIndexPage.cs` remain in core with untouched implementations.
- Began Phase 3 migration (T014-T018) by adding `VectorDistanceMetric`, `VectorIndexOptions`, and `VectorIndexService` to the `LiteDB.Vector` project and wiring `VectorIndexStrategy` to the new location; next iteration needs to finish detaching the legacy definitions from the core project and clean up duplicate type conflicts (T019-T020).

## 2025-11-03

- Completed Phase 3 core migration (T014-T024): moved `VectorDistanceMetric`, `VectorIndexOptions`, and the full `VectorIndexService` implementation into `LiteDB.Vector`, introduced a factory adapter so core components obtain services from the plugin, and removed all legacy source files and references from the LiteDB project.
- Updated downstream consumers to reference the plugin (benchmarks, demo tools, repository/collection APIs) and relocated the vector index integration tests into `LiteDB.Vector.Tests`, adding shared resources and project/package references needed for MathNet/System.Text.Json.
- Executed `dotnet build LiteDB.sln -c Release` and `dotnet test LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj` to validate the migrated code and ensure the plugin-backed vector query path stays green.
- Added plugin integration tests (T025) proving vector functionality remains disabled without `VectorSearchPlugin` and becomes available once registered, and backward-compatibility coverage (T026) confirming databases created prior to the migration reopen with existing vector indexes intact.
- Migrated the query pipeline to the plugin (T027-T029): relocated `VectorIndexQuery` under `LiteDB.Vector/Query`, introduced a vector planning rule that mirrors the previous core logic via `IQueryPlannerRegistry`, and updated `QueryOptimization` to consume plugin-provided plans while preserving vector order semantics.
- Removed the legacy query implementation from the core project (T030) and verified the new structure builds, noting the wider solution currently fails due to existing LiteDB.Tests references to internal `VectorExpressions`.
- Hardened `VectorIndexStrategy` lifecycle hooks (T031-T034): validated options parsing, wired plugin expression registry into `InsertVectorIndex`, short-circuited empty index scans, and enforced dimension/metric validation to guarantee HNSW graphs stay consistent across ensure/upsert/delete/drop flows.
- Added integration coverage for lifecycle and concurrency (T035-T036): `VectorIndexLifecycle_Tests` exercises create/search/drop/recreate semantics while inspecting underlying graph state (including document deletions removing nodes), and `VectorIndexConcurrency_Tests` stress concurrent updates vs. vector queries to validate snapshot isolation.

## 2025-11-04

- Verified the distance-metric configuration wiring for T038-T041: `VectorDistanceMetric` retains Euclidean/Cosine/DotProduct values, `VectorIndexService.ComputeDistance` covers all formulas, `VectorIndexOptions` defaults remain cosine, and `VectorSearchPlugin` respects the `vector.metric` connection-string override when instantiating `VectorIndexStrategy`.
- Added `LiteDB.Vector.Tests/VectorMetrics_Tests.cs` to satisfy T042-T044 with unit coverage for per-metric ranges, cross-metric ranking differences, and index creation using each metric (including metadata inspection via `VectorIndexService.Search`). `dotnet test LiteDB.Vector.Tests -f net8.0` currently fails on the pre-existing `VectorIndexLifecycle_Tests.EnsureIndex_BuildsGraph_And_DropCleansMetadata` assertion (expects 4 nodes after reindex, observes 3); root cause pending follow-up.
