# Plugin Context Isolation Plan (Remove LiteDatabaseServices.Default reliance)

## Goal
Ensure every `LiteDatabase` instance uses its own plugin context (registries for BSON types, page factories, index metadata, query operators, etc.), so two databases can load plugins with overlapping code spaces without conflicts. Keep `LiteDatabaseServices.Default` deleted and use `PluginContextFallbacks.Context` only for explicit legacy/no-DB helpers.

## Current Status
- `LiteDatabaseServices.Default` was removed from the codebase; only a minimal fallback lives in `PluginContextFallbacks` for truly legacy/no-DB helpers.
- All serialization/query paths (BufferReader/Writer, expression building, index metadata, mapper, LINQ visitor, query optimizations) now take the active database’s `ILitePluginContext` or the explicit fallback helper.
- Tests were updated to stop using the default context; default strict missing-plugin behavior now throws deterministic `LiteException` with plugin diagnostics instead of silently succeeding (behavior is host-controlled via `PluginMissingBehavior`).
- Full test matrix now passes (`net462` via xunit.console, `net481`, `net8.0`, `LiteDB.Vector.Tests`, `LiteDB.ReproRunner.Tests`).
- Missing-plugin diagnostics are stored under `Exception.Data["PluginDiagnostics"]` as a cloned `BsonDocument` for inspection across TFMs.
- Added isolation regression coverage for duplicate plugin IDs: two in-memory databases register the same function/BSON/page codes and return instance-local results while the fallback context stays clean.

## Remaining Issues
- Keep an eye on any straggling doc/spec references to `LiteDatabaseServices.Default`.
- Ensure new plugin work continues to thread per-db contexts; avoid accidental reintroduction of globals.


## Desired Behavior
- Each `LiteDatabase` instance passes its own `ILitePluginContext` to all serialization, query, and storage operations.
- No plugin type/page registrations bleed across instances.
- Missing-plugin diagnostics fire when a database lacks the required plugin, not masked by global defaults.
- `LiteDatabaseServices.Default` stays deleted; the only global helper is `PluginContextFallbacks.Context` for rare no-DB utilities with explicit rationale.

## Changes to Implement
1) **Keep globals out**
   - Code review gate: reject any new `LiteDatabaseServices.Default` usage; favor constructor-injected `ILitePluginContext` or explicit `PluginContextFallbacks.Context` when no DB exists.

2) **Per-instance registrations**
   - Ensure future plugin registrations (BSON types, page factories, query operators, cost models) stay scoped to `ILitePluginContext` passed into `Initialize`.

3) **Diagnostics**
   - Continue to surface deterministic missing-plugin `LiteException` with diagnostics stored under `Exception.Data["PluginDiagnostics"]` as a cloned `BsonDocument` across all TFMs.

4) **Tests & tooling**
   - Add/maintain isolation regression tests: two databases with overlapping plugin IDs must not see each other’s registrations.
   - Keep optionality/behavior-matrix tests aligned with host-controlled missing-plugin policy (default strict refusal at affected collection access; optional non-strict modes).
   - Re-run `scripts/run-tests-per-target.ps1` after isolation coverage to ensure the net462 xunit fallback path stays green.

## Implementation Order (suggested)
1. Guardrail reviews to prevent reintroduction of global defaults.
2. Add/expand isolation regression coverage (competing plugin IDs across two `LiteDatabase` instances).
3. Update `specs/001-vector-plugin-extraction/progress.md` with the Default removal and fallback note.
4. Keep test expectations in sync with strict missing-plugin refusal.
5. Periodically run full matrix (`scripts/run-tests-per-target.ps1`) to catch TFM differences (e.g., `Exception.Data` serialization on .NET Framework).

## Risks / Mitigations
- Risk: Null context in existing call sites → compile errors. Mitigation: plumb context from `LiteDatabase`/`Snapshot` where available; add explicit guard in rare static helpers.
- Risk: Some legacy paths (no database) genuinely need a default: keep a minimal default but never auto-register plugins into it.

## Done When
- No production code references `LiteDatabaseServices.Default.Context`; only `PluginContextFallbacks.Context` remains for legacy/no-DB helpers.
- Tests pass with per-instance plugin contexts; cross-instance plugin code conflicts are impossible.
