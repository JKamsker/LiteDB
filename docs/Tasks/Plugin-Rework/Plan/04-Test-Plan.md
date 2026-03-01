# Test plan (new/updated coverage)

## Builder / Factory

1. Builder smoke: build `:memory:` db with vector plugin; create vector index; query.
2. Factory (`FactoryReuse.None`):
   - Create factory for `:memory:`; create db1/db2; assert isolated contexts (e.g., plugin registrations don’t leak if you use per-build plugin factories).
3. Factory (`FactoryReuse.ReuseEngine`):
   - Create factory with reused engine/context; `CreateDatabase()` twice.
   - Verify plugin initialization ran once (use a tracking plugin).
   - Verify disposing db handles does not dispose the reused engine while the factory is alive.
   - Verify disposing the factory does not dispose the reused engine until all handles are disposed; engine is disposed exactly once when the last handle is disposed.
4. Double-dispose:
   - Disposing the same db handle twice must not underflow the lease/refcount and must not dispose the engine twice.
5. Dispose ordering:
   - Dispose factory first, then dispose db handles; ensure handles remain usable until disposed and the reused engine is disposed exactly once after the last handle is disposed.
6. Factory disposed behavior:
   - Disposing the factory prevents new handles: `CreateDatabase()` throws after factory disposal.
7. Concurrency:
   - In `FactoryReuse.ReuseEngine`, concurrent `CreateDatabase()` calls do not double-initialize plugins (use a tracking plugin and `Task.WhenAll`).

## Missing-plugin behavior (host-controlled)

8. Default (`RefuseDatabase`) unchanged: open db with plugin index but without plugin; accessing that collection throws `PLUGIN_REQUIRED`.
9. `RefuseOperations`:
   - Open db without plugin but with `RefuseOperations`; verify unaffected collections are usable, but reading/writing/DDL on an affected collection throws `PLUGIN_REQUIRED`.
10. `AllowIfSafe` + read snapshot:
   - Open db without plugin but with `AllowIfSafe`; verify reading does not attempt plugin indexes (after planner fix), including `OrderBy`/`GroupBy` paths that trigger fallback index selection.
11. `AllowIfSafe` + write/DDL attempts:
   - Insert/update/delete into a collection that contains plugin-owned indexes throws `PLUGIN_REQUIRED`.
   - Ensure/drop index, drop/rename collection, and drop collection on an affected collection throws `PLUGIN_REQUIRED` (and must refuse before partial deletion/page leaks).
12. Edge case (optional): `IndexType != 0` with missing/corrupt plugin metadata
   - Craft a database where a collection has a non-btree index but the plugin metadata entry is missing/corrupt; assert enforcement still triggers and diagnostics use the `<unknown>:type:{indexType}` warning key.

## Validation-on-open (opt-in)

13. Enable `ValidatePluginsOnOpen`, open db without plugin and with strict policy; expect failure before first operation on an affected collection (fail-fast at engine-open time; note `ConnectionType.Shared` may surface this on first operation rather than during `LiteDatabase` construction).
14. Enable `ValidatePluginsOnOpen` with `RefuseOperations`; open db without plugin; construction succeeds and unaffected collections remain usable (validation must not make the whole DB fail).
15. Enable `ValidatePluginsOnOpen` with `AllowIfSafe`, open db without plugin; expect warnings but construction succeeds.

## $plugins

16. With plugin present: `$plugins` lists pluginId, loaded=true, correct collections/indexCount.
17. Without plugin and strict policy: `$plugins` still lists pluginId, loaded=false (introspection must work without triggering missing-plugin enforcement or warn-once caches).

## Rebuild drop option

18. Seed file db with vector index:

- `db.Rebuild()` without plugin throws (existing tests should still pass).
- With `AllowIfSafe` (and with `RefuseOperations`) configured and the plugin missing, calling `db.Rebuild()` without `DropOrphanedPluginIndexes` throws `PLUGIN_REQUIRED` and the same `LiteDatabase` remains usable after the failure (engine not left stuck closed).
- `db.Rebuild(new RebuildOptions { DropOrphanedPluginIndexes = true })` succeeds without plugin when documents/pages are otherwise readable without the plugin (index salvage only).
- If documents contain plugin-defined BSON types/page dependencies (e.g., insert a `BsonVector` value), rebuild without the plugin fails by default (no implicit document skipping/data loss).
- Assert the dropped plugin index is absent after rebuild (no row for that index name) and re-creating it returns `true` (proves it was dropped).
- Assert `_rebuild_errors` (or equivalent rebuild report) contains an entry indicating the plugin index was dropped.
- Reopen with plugin and explicitly re-create vector index; ensure it works.

## Run verification

- `dotnet test LiteDB.sln --settings tests.runsettings`
- Optional full matrix: `pwsh -File scripts/run-tests-per-target.ps1`
