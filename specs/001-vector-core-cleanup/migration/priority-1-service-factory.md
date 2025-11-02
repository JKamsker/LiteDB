# Priority 1 – Relocate `VectorIndexServiceFactory`

## Objective
Move the vector service factory and related internals from `LiteDB/Engine/Services` into `LiteDB.Vector` so the plugin owns vector service activation without leaking core `InternalsVisibleTo` exemptions.

## Prerequisites
- Align with migration decision `move-to-plugin-short` and confirm plugin extension methods wrap public EnsureVectorIndex entry points.
- Prepare replacement factory inside `LiteDB.Vector` that mirrors current dependencies and exposes registration through plugin startup.

## Migration Sequence
1. Introduce a plugin-hosted `VectorIndexServiceFactory` implementation and wire it into the plugin bootstrapper.
2. Refactor core callers to resolve the factory via plugin interfaces rather than direct type construction.
3. Delete the core `InternalsVisibleTo` grant and redundant constants once the plugin factory ships.
4. Update integration tests to exercise the plugin-based service wiring.

## Compatibility
- Must preserve existing vector indexes without rebuild by maintaining service discovery semantics for legacy databases.

## Performance
- Must maintain ≤2% regression from current throughput; monitor index build benchmarks before and after relocation.

## Fallback Strategy
- Provide a guarded shim in core that throws a clear error when vector APIs are invoked without the plugin, pointing users to install `LiteDB.Vector`.

## Verification
- Run `verification/service-factory.json` once drafted plus the existing build and plugin validation steps to confirm service resolution works end-to-end.
