# Priority 2 – Deprecate Core `EnsureVectorIndex` APIs

## Objective
Transition public entry points that expose vector semantics (`ILiteEngine.EnsureVectorIndex`, `LiteQueryable.EnsureVectorIndex`, repository helpers) to plugin-owned extension methods so the core surface becomes plugin-agnostic.

## Prerequisites
- Short-term migration decision `move-to-plugin-short` satisfied with extension replacements ready in `LiteDB.Vector`.
- Documentation and samples prepared to steer consumers toward the plugin extension methods.

## Migration Sequence
1. Add plugin extension methods that mirror the current overloads and distance helper semantics.
2. Mark core `EnsureVectorIndex` methods and repository wrappers as `[Obsolete]` with guidance pointing to the plugin.
3. Ship a release where both core and plugin paths coexist, collecting telemetry on extension adoption.
4. Remove obsolete core methods once adoption targets are met and regression testing stays green.

## Compatibility
- Must preserve existing vector indexes without rebuild; ensure plugin extension methods interoperate with indexes created via the deprecated core APIs.

## Performance
- Must maintain ≤2% regression from current throughput by validating index creation and query benchmarks through both core-deprecated and plugin pathways during transition.

## Fallback Strategy
- When the plugin is absent, keep a lightweight guard that notifies callers that vector features require installing `LiteDB.Vector` rather than silently failing.

## Verification
- Use public API verification steps plus manual smoke tests covering extension method parity before flipping the final removal switch.
