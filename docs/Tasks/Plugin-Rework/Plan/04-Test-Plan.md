# Test plan (new/updated coverage)

## Builder / Factory

1. (P0) Builder smoke: build `:memory:` db with vector plugin; create vector index; query.
2. (P1) Factory (`FactoryReuse.None`):
   - Create factory for `:memory:`; create db1/db2; assert isolated contexts (e.g., plugin registrations don’t leak if you use per-build plugin factories).
3. (P0) Factory (`FactoryReuse.ReuseEngine`):
   - Create factory with reused engine/context; `CreateDatabase()` twice.
   - Verify plugin initialization ran once (use a tracking plugin).
   - Verify disposing db handles does not dispose the reused engine while the factory is alive.
   - Verify disposing the factory does not dispose the reused engine until all handles are disposed; engine is disposed exactly once when the last handle is disposed.
4. (P0) Double-dispose:
   - Disposing the same db handle twice must not underflow the lease/refcount and must not dispose the engine twice.
5. (P0) Dispose ordering:
   - Dispose factory first, then dispose db handles; ensure handles remain usable until disposed and the reused engine is disposed exactly once after the last handle is disposed.
6. (P0) Factory disposed behavior:
   - Disposing the factory prevents new handles: `CreateDatabase()` throws after factory disposal.
7. (P0) Concurrency:
   - In `FactoryReuse.ReuseEngine`, concurrent `CreateDatabase()` calls do not double-initialize plugins (use a tracking plugin and `Task.WhenAll`).

## Missing-plugin behavior (host-controlled)

8. (P0) Default (`RefuseDatabase`) unchanged: open db with a plugin-owned index but without plugin; accessing that collection throws `PLUGIN_REQUIRED`.
9. (P0) `RefuseOperations`:
   - Open db without plugin but with `RefuseOperations`; verify unaffected collections are usable, but reading/writing/DDL on an affected collection throws `PLUGIN_REQUIRED`.
10. (P0) `AllowIfSafe` + read snapshot + planner:
   - Open db without plugin but with `AllowIfSafe`; verify reading does not attempt plugin indexes (after planner fix), including `OrderBy`/`GroupBy` paths that trigger fallback index selection.
   - Assert the query plan’s chosen index is a btree index (`IndexType == 0`), not a plugin-owned index name.
11. (P0) `AllowIfSafe` + write/DDL attempts (must refuse before partial work):
   - Insert/update/delete into a collection that contains plugin-owned indexes throws `PLUGIN_REQUIRED`.
   - Ensure/drop index, drop/rename collection, and drop collection on an affected collection throws `PLUGIN_REQUIRED` (and must refuse before partial deletion/page leaks).
   - Reopen with the plugin and prove no partial mutation occurred (collection still exists, indexes still present; no orphaned/leaked pages from skipped plugin cleanup).
12. (P1) Edge case: `IndexType != 0` with missing/corrupt plugin metadata
   - Craft a database where a collection has a non-btree index but the plugin metadata entry is missing/corrupt; assert enforcement still triggers and diagnostics use the `<unknown>:type:{indexType}` warning key.

### Diagnostics + cache scoping

13. (P0) Warn-once cache scoped by database identity:
   - Open two different file databases (in the same process) with plugin-owned indexes, plugin missing, `AllowIfSafe`; first read of an affected collection in each file warns once per file (not suppressed globally).
14. (P0) `$plugins` does not poison missing-plugin enforcement/warn caches:
   - Under strict behavior, query `$plugins` first; then touch an affected collection under `AllowIfSafe` and assert the warning/refusal behavior is unchanged (first allowed read still warns once; write/DDL still refuses).
15. (P0) `$plugins` non-throwing under corruption:
   - Corrupt legacy/plugin metadata (or a collection page) so scanning triggers a parse failure; `$plugins` must not throw, must include an `errors[]` entry, and must still surface the affected collection under `<unknown>` when pluginId is not recoverable.
16. (P0) `ValidatePluginsOnOpen` does not poison missing-plugin enforcement/warn caches:
   - Enable `ValidatePluginsOnOpen` with `AllowIfSafe`, open db without the plugin; validation may warn, but the first actual read snapshot on an affected collection must still produce the expected warn-once behavior (not suppressed because validation ran).
17. (P1) Descriptor-only is not sufficient for write safety:
   - Register a custom-index descriptor for a pluginId, but do not register an `IIndexStrategy` for the persisted `IndexType`; writes/DDL to the affected collection must still throw `PLUGIN_REQUIRED`.

## Validation-on-open (opt-in)

18. (P0) Enable `ValidatePluginsOnOpen`, open db without plugin and with strict policy; expect failure before first operation on an affected collection (fail-fast at engine-open time; note `ConnectionType.Shared` may surface this on first operation rather than during `LiteDatabase` construction).
19. (P0) Enable `ValidatePluginsOnOpen` with `RefuseOperations`; open db without plugin; construction succeeds and unaffected collections remain usable (validation must not make the whole DB fail).
20. (P0) Enable `ValidatePluginsOnOpen` with `AllowIfSafe`, open db without plugin; expect warnings but construction succeeds.
21. (P0) `ConnectionType.Shared` + validation failure does not deadlock:
   - Trigger a validation-on-open failure under `SharedEngine` and then run another operation; it must fail fast (no hang) and must not leak the shared mutex/engine state.

## $plugins

22. (P0) With plugin present: `$plugins` lists pluginId, loaded=true, correct collections/indexCount.
23. (P0) Without plugin and strict policy: `$plugins` still lists pluginId, loaded=false (introspection must work without triggering missing-plugin enforcement or warn-once caches).

## Rebuild drop option

24. (P0) Seed file db with vector index:
   - `db.Rebuild()` without plugin throws (existing tests should still pass).
   - With `AllowIfSafe` (and with `RefuseOperations`) configured and the plugin missing, calling `db.Rebuild()` without `DropOrphanedPluginIndexes` throws `PLUGIN_REQUIRED` and the same `LiteDatabase` remains usable after the failure (engine not left stuck closed).
   - `db.Rebuild(new RebuildOptions { DropOrphanedPluginIndexes = true })` succeeds without plugin when documents/pages are otherwise readable without the plugin (index salvage only).
   - If documents contain plugin-defined BSON types/page dependencies (e.g., insert a `BsonVector` value), rebuild without the plugin fails by default (no implicit document skipping/data loss).
   - Assert the dropped plugin index is absent after rebuild (no row for that index name) and re-creating it returns `true` (proves it was dropped).
   - Assert the durable rebuild report (`_rebuild_errors` or equivalent) contains an entry indicating the plugin index was dropped (include `pluginId` or `<unknown>` + `indexType`).
   - Report enforcement: `DropOrphanedPluginIndexes=true` with `IncludeErrorReport=false` must be rejected or coerced so the durable drop entry exists.
   - Unknown-plugin/metadata-missing case: craft a database where `IndexType != 0` exists but plugin metadata is missing/corrupt; strict rebuild must still fail (or salvage must drop+report) and must never rebuild that index as btree.
   - Reopen with plugin and explicitly re-create vector index; ensure it works.

## Run verification

- `dotnet test LiteDB.sln --settings tests.runsettings`
- Optional full matrix: `pwsh -File scripts/run-tests-per-target.ps1`
