# Priority 3 – Design Query Metadata Extensions

## Objective
Provide a plugin-managed query metadata bag that lets `LiteDB.Vector` own similarity search parameters (`VectorField`, `VectorTarget`, `VectorMaxDistance`, `VectorMetric`) while the core query planner stays generic.

## Prerequisites
- Fulfil migration decision `requires-infrastructure` by delivering a plugin-owned query metadata container exposed through `QueryPlanningContext`.
- Document serialization format for metadata entries so other plugins can participate without collisions.

## Migration Sequence
1. Define the metadata bag contract (interfaces, lifecycle) surfaced through the plugin context.
2. Update `LiteDB.Engine.Query.Query` and related optimizers to use the bag instead of hard-coded vector properties.
3. Move vector-specific planning logic into `LiteDB.Vector`, relying on the bag for state transfer.
4. Validate LINQ and SQL query paths against the plugin-managed metadata to ensure parity.

## Compatibility
- Must preserve existing vector indexes without rebuild by keeping planner outputs compatible with on-disk index structures.

## Performance
- Must maintain ≤2% regression from current throughput; profile query planning and execution with metadata indirection to confirm negligible overhead.

## Fallback Strategy
- When the plugin is missing, default the metadata bag to an inert implementation that blocks vector-specific queries with actionable diagnostics.

## Verification
- Execute query planning verification steps along with targeted regression tests that assert metadata bag entries flow correctly between planner and executor.
