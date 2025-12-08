# Plugin Context Isolation Plan (Remove LiteDatabaseServices.Default reliance)

## Goal
Ensure every `LiteDatabase` instance uses its own plugin context (registries for BSON types, page factories, index metadata, query operators, etc.), so two databases can load plugins with overlapping code spaces without conflicts. Eliminate reliance on the singleton `LiteDatabaseServices.Default` except for legacy/no-plugin fallback paths.

## Current Issues
- `LiteDatabaseServices.Default.Context` is used as a fallback in many components (e.g., `BufferReader/BufferWriter`, expression creation, tests), causing shared global registries.
- Tests register plugin BSON types against the default context to make type 0x90 known, leaking state across runs.
- The design allows cross-instance collisions and hides missing-plugin scenarios because global registries already contain plugin types/factories.

## Desired Behavior
- Each `LiteDatabase` instance passes its own `ILitePluginContext` to all serialization, query, and storage operations.
- No plugin type/page registrations bleed across instances.
- Missing-plugin diagnostics fire when a database lacks the required plugin, not masked by global defaults.

## Changes to Implement
1) **Remove default context fallbacks**
   - Update `BufferReader`/`BufferWriter` constructors to require an explicit `ILitePluginContext` (or throw if null), and ensure callers pass the database’s context.
   - Audit other helpers that do `pluginContext ?? LiteDatabaseServices.Default.Context` (e.g., expression creation, index services) and route the active database context instead.

2) **Per-instance registrations**
   - Ensure plugin registrations (BSON types, page factories, query operators, cost models) happen only in `VectorSearchPlugin.Initialize` (or other plugins) against the provided `ILitePluginContext`, never against a global default.
   - Tests that need vector BSON (e.g., `BsonVector_Tests`) should register via the database instance they create, not through `LiteDatabaseServices.Default`.

3) **Default context scope**
   - Keep `LiteDatabaseServices.Default` only for “no database” static helpers that must work without plugins; document that it is not a registry for plugin types.
   - Consider internalizing or marking it obsolete to discourage use.

4) **Diagnostics**
   - Verify missing-plugin exceptions are thrown when a database lacks the required plugin (no default-context masking).

5) **Tests & tooling**
   - Update tests to pass plugin contexts explicitly and remove default-context registrations.
   - Add regression tests ensuring two databases with different plugin sets do not share registrations (e.g., vector plugin registered in db A does not make type 0x90 available in db B).

## Implementation Order (suggested)
1. Refactor `BufferReader/BufferWriter` to require context; fix call sites.
2. Remove `LiteDatabaseServices.Default.Context` fallbacks in serialization/expression paths.
3. Update tests to use per-instance plugin registration; add isolation regression test.
4. Re-run full test matrix; fix remaining missing-plugin message expectations.

## Risks / Mitigations
- Risk: Null context in existing call sites → compile errors. Mitigation: plumb context from `LiteDatabase`/`Snapshot` where available; add explicit guard in rare static helpers.
- Risk: Some legacy paths (no database) genuinely need a default: keep a minimal default but never auto-register plugins into it.

## Done When
- No production code references `LiteDatabaseServices.Default.Context` except designated legacy/no-db helpers.
- Tests pass with per-instance plugin contexts; cross-instance plugin code conflicts are impossible.
