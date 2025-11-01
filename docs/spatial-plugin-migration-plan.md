# Spatial Plugin Migration Plan

This document captures the high-level plan for moving spatial features out of the core `LiteDB` project and into the new plugin infrastructure.

## Phase 1 – Extend Core Plugin Hooks
- Introduce a LINQ resolver registry on the plugin context so plugins can register `Type -> ITypeResolver` mappings. Adjust `LiteDB/Client/Mapper/Linq/LinqExpressionVisitor.cs` to consult the registry before falling back to its built-in resolver map.
- Expose an extension point for predicate classification/query rewrites (e.g., through `IQueryPlanningRule` or a new callback) and update `LiteDB/Engine/Query/QueryOptimization.cs` to rely on plugin-provided logic instead of hard-coded spatial checks.
- Ensure `DefaultPluginContext` owns the new registries and that `LiteDatabase.Services` publishes them, mirroring the existing expression and index registries.
- Document the new extension points so plugins can discover how to participate.

## Phase 2 – Strip Spatial Logic from Core
- Remove spatial resolvers from the core, including the static entries and `LiteDB/Client/Mapper/Linq/TypeResolver/SpatialResolver.cs`, once registry-based lookups exist.
- Delete `LiteDB/Document/Expression/Methods/Spatial.cs` and relocate any required helpers into the spatial plugin, relying on plugin-registered delegates instead of `BsonExpressionMethods`.
- Excise the `IsSpatialPredicate` helper from `LiteDB/Engine/Query/QueryOptimization.cs`, replacing it with plugin-supplied detection via the new hook while preserving non-spatial behavior.
- Remove the entire `LiteDB/Spatial` namespace from the base project, updating `LiteDB.csproj` and clearing related `using` directives.

## Phase 3 – Build the Spatial Plugin Package
- Implement an `ILitePlugin` (e.g., `LiteDB.Spatial/SpatialPlugin.cs`) that registers spatial expression functions, keywords, index strategies, and query-planning rules through the expanded plugin context.
- Move geometry, metadata, and engine code currently under `LiteDB/Spatial` into the plugin projects (`LiteDB.Spatial.Core`, `LiteDB.Spatial.Cartesian2D`, `LiteDB.Spatial.Cartesian3D`, etc.), aligning namespaces with their new home.
- Register LINQ resolvers (the relocated `SpatialResolver`) using the new registry so spatial LINQ queries continue to translate without direct core dependencies.
- Ensure the plugin configures BSON mapping for spatial types (e.g., via `database.Mapper.RegisterType`) and provide opt-in helpers for consumers to enable spatial support.

## Phase 4 – Validation and Rollout
- Update unit and integration tests to construct `LiteDatabase` instances with the spatial plugin, adding coverage for both plugin-enabled and plugin-absent scenarios.
- Run `dotnet test LiteDB.sln --settings tests.runsettings` and spatial-focused suites to confirm behavior parity; refresh benchmarks and samples to reference the plugin package.
- Revise documentation (`docs/spatial-*.md`, README snippets, sample programs) to describe installation and activation of the spatial plugin and migration steps for existing users.
- Adjust packaging metadata and CI workflows so `LiteDB.Spatial` publishes as a plugin-aligned NuGet package that depends on the plugin-friendly core.
