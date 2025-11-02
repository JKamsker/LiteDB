# Priority 4 – Extend Plugin for Custom Storage Pages and BSON Types

## Objective
Enable `LiteDB.Vector` to own vector storage primitives by introducing plugin hooks for custom page factories, metadata serialization, and BSON type registration.

## Prerequisites
- Implement plugin-managed BSON type registration so `BsonType.Vector`, `BsonValue.AsVector`, and related serializers can relocate without breaking compatibility.
- Design a plugin-accessible page factory and metadata API that lets the plugin supply `VectorIndexPage` and `VectorIndexMetadata` definitions.
- Align with migration decision `requires-infrastructure` and ensure storage pipeline diagnostics continue to function with plugin-provided pages.

## Migration Sequence
1. Add a BSON type registry to the plugin system and migrate vector serializers into `LiteDB.Vector`.
2. Introduce page factory interfaces and register vector-specific pages from the plugin.
3. Update rebuild, snapshot, and file reader routines to invoke plugin factories and metadata providers.
4. Remove core vector page classes once plugin handling passes verification.

## Compatibility
- Must preserve existing vector indexes without rebuild by honoring on-disk page layout identifiers and BSON type codes during the move.

## Performance
- Must maintain ≤2% regression from current throughput; run endurance tests covering index rebuilds and serialization-heavy workloads after migration.

## Fallback Strategy
- Supply a compatibility layer that keeps core readers able to detect legacy vector pages and instruct users to enable the plugin before modifying data.

## Verification
- Combine storage and serialization verification steps to ensure page materialization, BSON conversions, and rebuild flows continue to succeed.
