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
