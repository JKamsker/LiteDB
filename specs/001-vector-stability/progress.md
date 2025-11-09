# Progress Log - Vector Stability Hardening

## 2025-11-10 00:30
- Completed T001 by creating `specs/001-vector-stability/research.md` with regression summaries for dot-product normalization, legacy API removal, plugin scope/isolation, and artifact cleanup contexts.
- Completed T002 by creating `specs/001-vector-stability/quickstart.md`, outlining US1 fail-first steps plus the full dotnet/git verification loop to reuse during later phases.

## 2025-11-10 00:40
- Completed T003 by adding `LiteDB.Vector.Tests/Infrastructure/VectorTestContext.cs`, which seeds vector collections, normalizes expressions, configures metadata bags, and exposes helpers for `WhereNear`, `TopKNear`, and metadata-driven similarity queries; verified via `dotnet build LiteDB.Vector.Tests/LiteDB.Vector.Tests.csproj -c Debug`.

## 2025-11-10 00:44
- Completed T004 by extending `LiteDB.Tests/Utils/DatabaseFactory.cs` with `DatabaseFactoryOptions`, `LiteDatabaseGroup`, and `CreateMany`, enabling multi-instance LiteDatabase setups plus coordinated disposal; added `LiteDB.Tests/Utils/DatabaseFactoryTests.cs` to prove independent state and per-instance plugin wiring, confirmed with `dotnet test LiteDB.Tests/LiteDB.Tests.csproj -f net8.0 --filter DatabaseFactoryTests`.
