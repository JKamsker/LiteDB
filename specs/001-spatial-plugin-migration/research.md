# Phase 0 Research – Spatial Plugin Migration

## Decision: Implement factory-based LINQ resolver registry with per-database memoization
- **Rationale**: Spatial and vector plugins need access to the active `LiteDatabase` (e.g., to hydrate descriptors) when supplying resolvers. Accepting factories lets plugins capture database-specific state, while memoizing `(LiteDatabase, Type)` pairs inside `LinqExpressionVisitor` prevents repeated factory invocation during query translation.
- **Alternatives considered**:
  - **Static resolver instances**: Simplest but prevents database-specific state and would force plugins to manage global caches, increasing coupling.
  - **Service locator on plugin context**: Adds indirection without solving the need for per-database resolver instantiation.

## Decision: Extend `IQueryPlanningRule` with a structured `QueryPlanningContext`
- **Rationale**: Both spatial and vector plugins require insight into parsed terms, snapshot metadata, and plugin registries. Supplying a context object allows rules to both claim predicates and influence plan construction while keeping the core extensible for future plugins (e.g., FTS).
- **Alternatives considered**:
  - **Predicate classifier registry**: Lightweight but would duplicate logic when planning rules also need to rewrite query plans.
  - **Hard-coded spatial handling**: Violates plugin-first principle and prevents other plugins from sharing infrastructure.

## Decision: Maintain compatibility shims for legacy spatial metadata inside plugin
- **Rationale**: Internal databases created prior to public release may contain spatial metadata documents. Keeping the interpreter inside the plugin ensures they remain readable without core awareness, easing rollout.
- **Alternatives considered**:
  - **Core-level migration scripts**: Contradicts the goal of removing spatial code from core and complicates future plugin maintenance.
  - **Dropping metadata support**: Risks data loss for internal adopters and complicates upgrade testing.

## Decision: Introduce plugin-managed index interceptors for `EnsureIndex`
- **Rationale**: Spatial indexing should be configured using the standard `EnsureIndex` surface. An interceptor pipeline allows the plugin to detect `GeoPoint` fields and swap in spatial index logic without bespoke helpers such as `Spatial.UseGeographic`.
- **Alternatives considered**:
  - **Dedicated spatial helper methods**: Adds redundant APIs and increases risk of divergence between core and plugin.
  - **Core modifications to detect spatial types directly**: Reintroduces spatial awareness into core, defeating the migration goal.

## Decision: Provide plugin-scoped LINQ extensions (e.g., `WhereNear`)
- **Rationale**: Extension methods on `ILiteQueryable` keep spatial query ergonomics high while avoiding namespace collisions and keeping `SpatialExpressions.*` available for advanced usage.
- **Alternatives considered**:
  - **Rely solely on static `SpatialExpressions.*`**: Works but forces consumers to manage namespaces that may collide with other libraries and is less discoverable.
  - **Embed query helpers in core**: Not acceptable once spatial moves out of the base library.
