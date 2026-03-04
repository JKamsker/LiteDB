# Phase 1 Data Model – Spatial Plugin Migration

## Entities

### Plugin Extension Registries
- **Description**: Collection of registries (`Expressions`, `Indexes`, `LinqResolvers`, `QueryPlanner`) exposed via `ILitePluginContext`.
- **Core Fields**:
  - `Expressions`: map of operator/function registrations.
  - `Indexes`: registered `IIndexStrategy` instances keyed by kind/type code.
  - `IndexInterceptors`: optional registry mapping predicates (e.g., type + options) to interceptor delegates invoked during `EnsureIndex`.
  - `LinqResolvers`: dictionary of `Type` → resolver factory delegates.
  - `QueryPlanner`: ordered list of `IQueryPlanningRule` implementations.
- **Relationships**:
  - Owned by `LiteDatabaseServices`.
  - Consumed by plugins during `ILitePlugin.Initialize`.
- **Validation Rules**:
  - Duplicate keys overwrite previous entries but must log diagnostics.
  - Factories must return non-null resolvers; null results raise initialization errors.
  - Interceptors must declare compatibility (e.g., supported data types); conflicts are resolved via registration order or explicit priority.

### Query Planning Context
- **Description**: Structured object provided to `IQueryPlanningRule` instances during query optimization.
- **Core Fields**:
  - `Snapshot`: reference to current engine snapshot.
  - `Query`: immutable view of `Query` being planned.
  - `Terms`: list of parsed predicate expressions.
  - `PluginContext`: read-only view of `ILitePluginContext`.
  - `Services`: optional service provider for diagnostics.
- **Relationships**:
  - Created by `QueryOptimization`.
  - Passed to each registered planning rule in order.
- **State Transitions**:
  - Rules may return rewrites capturing selected index metadata and residual predicates.

### Spatial Plugin Package
- **Description**: Aggregates spatial engines, metadata stores, expression handlers, and implements `ILitePlugin`.
- **Core Fields**:
  - `SpatialPlugin` (`ILitePlugin`): orchestrates registrations.
  - `SpatialResolverFactory`: produces LINQ resolvers with collection descriptors.
  - `SpatialIndexInterceptor`: delegate invoked by `EnsureIndex` to configure spatial indexes based on field type/options.
  - `SpatialQueryableExtensions`: static class exposing helpers like `WhereNear` that wrap plugin expressions.
  - `SpatialExpressionHandlers`: static implementations of spatial expressions.
  - `SpatialIndexStrategy`: plugin index strategy for spatial data structures.
- **Relationships**:
  - Depends on plugin extension registries.
  - Integrates with spatial test suites (`LiteDB.Spatial.Core.Tests`).
- **Validation Rules**:
  - Initialization must succeed even when no spatial metadata exists.
  - Failure to initialize should emit actionable logger output.

### Enablement Documentation
- **Description**: Markdown guidance describing how to add and configure the spatial plugin.
- **Core Fields**:
  - `Prerequisites`: required packages/targets.
  - `Setup Steps`: code snippets showing plugin registration with `LiteDatabase`.
  - `Verification`: commands/tests ensuring successful setup.
- **Relationships**:
  - Referenced from release notes and README updates.

## Derived/Supporting Concepts

- **Resolver Memoization Cache**
  - Maps `(LiteDatabase, Type)` to instantiated resolvers.
  - Ensures thread-safe access (e.g., `ConditionalWeakTable` or `ConcurrentDictionary`).

- **Index Interceptor Pipeline**
  - Allows plugins to inspect `EnsureIndex` requests and replace execution when predicates match (e.g., field type `GeoPoint`).
  - Must fall back to core behavior if no interceptor matches.

- **Queryable Extension Layer**
  - Extension methods on `ILiteQueryable<T>` (e.g., `WhereNear`, `WhereWithin`) that compose plugin expressions while avoiding naming collisions with other libraries.

- **Diagnostic Logging**
  - Plugin context exposes `ILogger` for initialization-time diagnostics.
  - Spatial plugin uses it to report missing metadata or migration hints.

- **Test Fixtures**
  - Core tests verify absence of spatial features when plugin disabled.
  - Spatial plugin tests cover LINQ translation, expression evaluation, index integration.
