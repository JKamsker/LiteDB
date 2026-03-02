# Test plan (new/updated coverage)

## Test infrastructure

- [ ] **Tracking plugin**: Create a reusable `TestTrackingPlugin` that records: initialization count, initialization thread IDs, captured `LiteDatabase` references, and whether `Initialize` was called concurrently.
- [ ] **Seed helper**: Shared utility method for the common pattern: seed database with plugin → close → reopen without plugin → assert behavior.
- [ ] **Race helper**: For concurrency/race tests, use a barrier + loop (N iterations) + explicit timeouts; define the only acceptable exception types and assert “no hang / no leaked mutex/locks”.
- [ ] **Corruption helper**: Craft corruption deterministically (byte-level mutation of known offsets/fields), not random mutation. Prefer a net8-only helper if needed.
- [ ] **Parameterized behavior tests**: Use `[Theory]` with `[InlineData]` to cover the (behavior mode) x (operation type) matrix.

## Builder / Factory

1. [ ] (P0) Builder smoke: build `:memory:` db with vector plugin; create vector index; query.
   - [ ] Failure cleanup: if a plugin throws from `Initialize()` during `Build()`, the build fails without leaking engine/file handles/mutexes, and factory-created plugins are disposed.
2. [ ] (P1) Factory (shared engine):
   - [ ] Create factory for `:memory:`; `CreateDatabase()` twice.
   - [ ] Verify plugin initialization ran once (use tracking plugin).
   - [ ] Verify disposing db handles does not dispose the reused engine while the factory is alive.
   - [ ] Verify disposing the factory does not dispose the reused engine until all handles are disposed; engine is disposed exactly once when the last handle is disposed.
   - [ ] Failure cleanup: if a plugin throws from `Initialize()` during `BuildFactory()`, the build fails without leaking engine/file handles/mutexes, and factory-created plugins are disposed.
3. [ ] (P0) Double-dispose:
   - [ ] Disposing the same db handle twice must not underflow the lease/refcount and must not dispose the engine twice.
   - [ ] Concurrency variant: two threads disposing the same handle concurrently does not underflow refcount and does not double-dispose the engine.
4. [ ] (P0) Dispose ordering:
   - [ ] Dispose factory first, then dispose db handles; ensure handles remain usable until disposed and the reused engine is disposed exactly once after the last handle is disposed.
5. [ ] (P0) Factory disposed behavior:
   - [ ] Disposing the factory prevents new handles: `CreateDatabase()` throws `ObjectDisposedException` after factory disposal.
6. [ ] (P0) Concurrency -- CreateDatabase:
   - [ ] In factory mode, concurrent `CreateDatabase()` calls do not double-initialize plugins (use tracking plugin and `Task.WhenAll`).
7. [ ] (P0) Concurrency -- dispose + CreateDatabase race:
   - [ ] Start `factory.Dispose()` racing against `factory.CreateDatabase()` from another thread.
   - [ ] Either `CreateDatabase()` succeeds and returns a usable handle, or it throws. No deadlock, no corrupted refcount, no double-dispose.
8. [ ] (P0) Builder validation:
   - [ ] Second data-source call throws `InvalidOperationException` (not "last call wins").
   - [ ] `UseEngine(...)` + `WithConnectionType(...)` throws.
   - [ ] `UseStream(...)` + `BuildFactory()` throws.
   - [ ] `UseFile(...)` + `BuildFactory()` + `ConnectionType.Direct` (default) SUCCEEDS (safe: one engine, multiple handles).
   - [ ] `UseFile(...)` + `WithConnectionType(ConnectionType.Shared)` + `BuildFactory()` SUCCEEDS (factory sharing is in-process; mutex mode is still a valid cross-process setting).
   - [ ] Second `Build()`/`BuildFactory()` call on same builder throws.
9. [ ] (P0) Plugin duplicate registration:
   - [ ] Register same plugin type twice → first instance used; builder/factory may optionally log a warning (assert count/level, not message text).
10. [ ] (P1) Plugin disposal ownership:
    - [ ] `UsePlugin(Func<ILitePlugin>)` factory → created plugin is disposed when database/factory disposes.
    - [ ] `UsePlugin(ILitePlugin instance)` → plugin is NOT disposed when database/factory disposes.

## Missing-plugin behavior (host-controlled)

11. [ ] (P0) Default (`RefuseDatabase`) unchanged: open db with a plugin-owned index but without plugin; accessing that collection throws `PLUGIN_REQUIRED`.
12. [ ] (P0) `AllowIfSafe` + read snapshot + planner:
    - [ ] Open db without plugin but with `AllowIfSafe`; verify reading does not attempt plugin indexes (after planner fix), including `OrderBy`/`GroupBy` paths that trigger fallback index selection.
    - [ ] Create a plugin index with a distinctive name and assert `GetPlan()` does NOT select it (e.g., `plan["index"]["name"] != pluginIndexName`; planner uses `_id`, a btree index, or falls back to full scan).
    - [ ] Include a `Count()` operation on the affected collection (should succeed using `_id` index).
    - [ ] Clarify limitation: reads may still fail if documents require plugin-defined BSON/page support; add a test that demonstrates the failure mode is deterministic and does not corrupt the database.
13. [ ] (P0) `AllowIfSafe` + write/DDL attempts (must refuse before partial work):
    - [ ] Insert/update/delete/upsert into a collection that contains plugin-owned indexes throws `PLUGIN_REQUIRED`.
    - [ ] Ensure/drop index, drop/rename collection on an affected collection throws `PLUGIN_REQUIRED` (and must refuse before partial deletion/page leaks).
    - [ ] Reopen with the plugin and prove no partial mutation occurred (collection still exists, indexes still present; no orphaned/leaked pages).
14. [ ] (P0) `AllowIfSafe` + `ForUpdate` query:
    - [ ] Execute a query with `ForUpdate = true` on an affected collection; must throw `PLUGIN_REQUIRED` (opens `LockMode.Write` snapshot).
15. [ ] (P0) `AllowIfSafe` + write to unaffected collection:
    - [ ] Database has two collections: `clean` (no plugin indexes) and `affected` (has plugin-owned index). Open with `AllowIfSafe`. Insert/read `clean` → succeeds. Insert `affected` → throws.
    - [ ] DDL on `clean` succeeds (ensure/drop btree index, rename/drop collection) while the same operations on `affected` are refused.
16. [ ] (P0) Edge case: `IndexType != 0` with missing/corrupt plugin metadata
    - [ ] Craft a database where a collection has a non-btree index but the plugin metadata entry is missing/corrupt; assert enforcement still triggers and diagnostics include `indexType` details (do not hardcode the exact warn-cache key format).
17. [ ] (P0) Descriptor-only is not sufficient for write safety:
    - [ ] Register a custom-index descriptor for a pluginId, but do not register an `IIndexStrategy` for the persisted `IndexType`; writes/DDL to the affected collection must still throw `PLUGIN_REQUIRED`.

### Diagnostics + cache scoping

18. [ ] (P0) Warn-once cache scoped per database identity (not process-global):
    - [ ] Open two different file databases (in the same process) with plugin-owned indexes, plugin missing, `AllowIfSafe`; first read of an affected collection in each file warns once per file (not suppressed globally).
    - [ ] In a single database, touching two affected collections owned by the same plugin key warns once; touching affected collections owned by two different plugin keys warns once per key.
    - [ ] Verify the current process-global cache bug is fixed.
19. [ ] (P0) `$plugins` does not poison missing-plugin enforcement/warn caches:
    - [ ] Under strict behavior, query `$plugins` first; then touch an affected collection under `AllowIfSafe` and assert the warning/refusal behavior is unchanged.
20. [ ] (P0) `$plugins` non-throwing under corruption:
    - [ ] Corrupt legacy/plugin metadata (or a collection page) so scanning triggers a parse failure; `$plugins` must not throw, must include an `errors[]` entry, and must still surface the affected collection under the appropriate unknown/type row (`key = "type:{indexType}"`).
    - [ ] Orphan metadata case: metadata-without-index is surfaced in `errors[]` and does not by itself change enforcement if `IndexType == 0` (metadata is diagnostic-only).
21. [ ] (P0) `ValidatePluginsOnOpen` does not poison missing-plugin enforcement/warn caches:
    - [x] Enable `ValidatePluginsOnOpen` with `AllowIfSafe`, open db without the plugin; validation records diagnostics but does not warn or poison warn-once behavior. First real affected-collection access produces the expected warn-once behavior.

## Validation-on-open

22. [ ] (P0) `ValidatePluginsOnOpen` + `RefuseDatabase`:
    - [ ] Default/unset: does not fail-fast on open; failure occurs when first accessing an affected collection (legacy behavior).
    - [ ] Enabled: expect failure before first operation (fail-fast). Under `ConnectionType.Direct` this can fail during construction; under `ConnectionType.Shared` it fails on the first operation.
    - [ ] Failure diagnostics parity: the thrown exception includes the same requirement diagnostics that `$plugins`/typed API would report (since `$plugins` may be unreachable when validation fails during open).
    - [ ] Resource cleanup: after a fail-fast validation failure during construction, the underlying file/engine is not left locked; the database can be reopened normally in a subsequent attempt.
23. [x] (P0) `ValidatePluginsOnOpen=true` + `AllowIfSafe`: construction succeeds; diagnostics recorded; no warnings emitted during validation.
24. [ ] (P0) `ConnectionType.Shared` + validation failure does not deadlock:
    - [ ] Trigger a validation-on-open failure under `SharedEngine` and then run another operation; it must fail fast (no hang) and must not leak the shared mutex/engine state.
25. [ ] (P0) `SharedEngine` + `AllowIfSafe` + write attempt:
    - [ ] Open with `ConnectionType.Shared` and `AllowIfSafe`. Read from affected collection → succeeds (with warning). Insert → throws `PLUGIN_REQUIRED`. SharedEngine correctly releases mutex. Subsequent read still works.
26. [ ] (P0) `SharedEngine` + Rebuild + missing plugin:
    - [ ] Open with `ConnectionType.Shared`. Call `db.Rebuild()` with plugin-owned index present, plugin missing. Throws `PLUGIN_REQUIRED`. SharedEngine mutex released. Database still usable afterward.
27. [ ] (P1) `ValidatePluginsOnOpen` with `ConnectionType.Shared` timing:
    - [ ] Verify validation failure occurs on first operation (not during construction) under `SharedEngine`. Error message is deterministic. Subsequent operation also fails cleanly.

## $plugins

28. [ ] (P0) With plugin present: `$plugins` lists pluginId, loaded=true, correct collections/indexCount.
    - [ ] Verify `key == pluginId` and `strategyAvailable == true` when the plugin provides the required index strategy.
29. [ ] (P0) Without plugin and strict policy: `$plugins` still reports requirements with `loaded=false` (introspection works without triggering enforcement).
30. [ ] (P1) Multiple collections, some affected, some not: `$plugins` only reports affected collections, grouping unknown cases by `key = "type:{indexType}"` (not collapsing distinct unknown index types into a single row).

## Rebuild drop option

31. [ ] (P0) Seed file db with vector index:
    - [ ] `db.Rebuild()` without plugin throws (existing tests should still pass).
    - [ ] With `AllowIfSafe` configured and plugin missing, `db.Rebuild()` without `DropOrphanedPluginIndexes` throws `PLUGIN_REQUIRED` (rebuild is always DDL-strict).
    - [ ] **Same `LiteDatabase` remains usable after rebuild failure** (engine not left stuck closed).
    - [ ] `db.Rebuild(new RebuildOptions { DropOrphanedPluginIndexes = true })` succeeds without plugin when documents/pages are readable.
    - [ ] If documents contain plugin-defined BSON types, rebuild without plugin fails (no implicit document skipping).
    - [ ] Assert dropped plugin index is absent after rebuild; re-creating it returns true.
    - [ ] Assert durable rebuild report contains the drop entry.
    - [ ] Report enforcement: `DropOrphanedPluginIndexes=true` with `IncludeErrorReport=false` throws `InvalidOperationException` (audit trail required).
    - [ ] Unknown-plugin/metadata-missing case: craft db with `IndexType != 0` but no metadata; strict rebuild fails; salvage drops+reports; **never** rebuilt as btree.
    - [ ] Reopen with plugin, re-create vector index, verify it works and produces correct query results on all existing documents.

32. [ ] (P0) Recovery (auto-rebuild) remains strict:
    - [ ] Corrupt a database with plugin-owned indexes. Open with auto-recovery. Without plugin, recovery fails fast with `PLUGIN_REQUIRED` (not silently swallowed by `FileReaderV8.Open()` catch-all).

## Core planner fix (existing bug)

33. [ ] (P0) `ChooseIndex` with plugin-owned indexes in predicate matching:
    - [ ] Create collection with both a btree index and a plugin index (distinctive name). Query with predicate matching the plugin index's expression. Assert the query plan selects the btree index (or full scan) and `plan["index"]["name"] != pluginIndexName`.
34. [ ] (P0) `ChooseIndex` with plugin-owned indexes in OrderBy/GroupBy fallback:
    - [ ] Create collection with a plugin index on field X (distinctive name). Query with `OrderBy` on X. Assert the planner does NOT select it (`plan["index"]["name"] != pluginIndexName`).

## Rebuild + factory interaction

35. [ ] (P0) Factory mode + `Rebuild()`:
    - [ ] In factory mode with active handles, `Rebuild()` must be refused (or require exclusive access). Verify the engine is not closed while other handles are active.

## Existing test migration

36. [ ] (P0) `BehaviorMatrixIntegrationTests`:
    - [ ] Uses reflection to access `Snapshot._missingPluginWarnings` (static field). After rework changes the cache to db-scoped, this test must be updated. Verify all tests in this class pass after migration.
37. [ ] (P0) `VectorMetadataCompatibilityTests`:
    - [ ] Same reflection-based cache access pattern. Must be updated.
38. [ ] (P1) `VectorOperatorOptionalityTests.Planner_should_ignore_vector_indexes_without_plugin`:
    - [ ] Currently expects `PLUGIN_REQUIRED` under default mode. After rework, behavior under `RefuseDatabase` is unchanged. Add parameterized variant for `AllowIfSafe` where the query should succeed.
39. [ ] (P1) `PluginAbsentTests.EnsureVectorIndex_WithoutPlugin_ThrowsDeterministicError`:
    - [ ] Check if `exception.Data["VectorDiagnostics"]` key changes due to diagnostic policy cleanup (key might change to `"PluginDiagnostics"`).

## Edge cases

40. [ ] (P1) Empty database (no collections) with `ValidatePluginsOnOpen` → validation completes successfully, no warnings.
41. [ ] (P0) Index strategy collision guards:
    - [ ] Plugin that registers `IIndexStrategy` with `IndexType == 0` (btree collision) → registration rejected or throws `InvalidOperationException`. (IndexType 0 collision corrupts the btree system.)
    - [ ] Plugin that registers `IIndexStrategy` with a duplicate nonzero `IndexType` already registered by another plugin → rejected or throws (prevents silent overwrite/version mismatch).
42. [ ] (P0) INCLUDE (document reference lookup) opens snapshot on affected collection under `AllowIfSafe` → read snapshot, should succeed. Under `RefuseDatabase`, INCLUDE target on affected collection → throws. (Elevated from P1: cross-collection safety boundary in realistic production scenario.)
43. [ ] (P0) Plugin that captures `LiteDatabase` in `Initialize` under factory mode → test documents/validates the constraint. (Elevated from P1: prerequisite for factory mode, which is P0.)

## Registry freezing

44. [ ] (P0) After `Build()` or `BuildFactory()`, attempt to register a new index strategy or descriptor via the plugin context → throws `InvalidOperationException`.
45. [ ] (P0) After initialization, call `SetDiagnosticPolicy` on a frozen context → throws `InvalidOperationException`.
46. [ ] (P1) In `Build()` (single-database) mode, registries are also frozen after initialization (uniform contract across all modes).

## Additional factory tests

47. [ ] (P1) `UseEngine(engine)` + `BuildFactory()` path:
    - [ ] Provide a pre-created `LiteEngine` via `UseEngine(engine)`. Call `BuildFactory()`. Create handles. Verify plugin context is set.
    - [ ] `ownsEngine: false` + dispose factory + all handles → engine is NOT disposed.
    - [ ] `ownsEngine: true` + dispose factory + all handles → engine IS disposed.
48. [ ] (P0) Write-snapshot refusal does not leave dangling lock:
    - [ ] Under `AllowIfSafe`, open a write snapshot on an affected collection (which should be refused). Verify no lock is leaked. Subsequent operations on unaffected collections still work.
    - [ ] Exception safety: if snapshot construction throws after acquiring a collection lock (for example, a simulated collection-page parse failure), verify the lock is still released (no deadlock on subsequent operations).

## Regression guards

49. [ ] (P0) Existing constructor path still works:
    - [ ] Use `new LiteDatabase(connectionString, plugins: ...)` constructor path. Verify `MissingPluginBehavior` defaults to `RefuseDatabase` and `ValidatePluginsOnOpen` defaults to disabled (`null` → `false`) through the old API.
50. [ ] (P1) `IPluginDiagnosticPolicy.MissingBehavior` is actually ignored:
    - [ ] Register a plugin with diagnostic policy `MissingBehavior = AllowIfSafe`, but host sets `MissingPluginBehavior = RefuseDatabase`. Verify host setting wins (database is refused).
51. [ ] (P1) `RefuseOperations` backward compatibility:
    - [ ] Set `PluginMissingBehavior.RefuseOperations` (deprecated value). Verify it is treated as `AllowIfSafe` at runtime (reads allowed, writes refused).

## Run verification

- [ ] `dotnet test LiteDB.sln --settings tests.runsettings`
- [ ] Optional full matrix: `pwsh -File scripts/run-tests-per-target.ps1`
