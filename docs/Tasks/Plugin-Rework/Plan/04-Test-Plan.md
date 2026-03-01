# Test plan (new/updated coverage)

## Builder / Factory

1. Builder smoke: build `:memory:` db with vector plugin; create vector index; query.
2. Factory (`FactoryReuse.None`):
   - Create factory for `:memory:`; create db1/db2; assert isolated contexts (e.g., plugin registrations don’t leak if you use per-build plugin factories).
3. Factory (`FactoryReuse.ReuseEngine`):
   - Create factory with reused engine/context; `CreateDatabase()` twice.
   - Verify plugin initialization ran once (use a tracking plugin).
   - Verify disposing db handles does not dispose the shared engine while the factory is alive.
   - Verify disposing the factory does not dispose the shared engine until all handles are disposed; engine is disposed exactly once when the last handle is disposed.
4. Double-dispose:
   - Disposing the same db handle twice must not underflow the lease/refcount and must not dispose the engine twice.
5. Dispose ordering:
   - Dispose factory first, then dispose db handles; ensure handles remain usable until disposed and the shared engine is disposed exactly once after the last handle is disposed.

## Missing-plugin behavior (host-controlled)

6. Default (`RefuseDatabase`) unchanged: open db with plugin index but without plugin; accessing that collection throws `PLUGIN_REQUIRED`.
7. `RefuseOperations`:
   - Open db without plugin but with `RefuseOperations`; verify unaffected collections are usable, but reading/writing/DDL on an affected collection throws `PLUGIN_REQUIRED`.
8. `AllowIfSafe` + read snapshot:
   - Open db without plugin but with `AllowIfSafe`; verify reading does not attempt plugin indexes (after planner fix), including `OrderBy`/`GroupBy` paths that trigger fallback index selection.
9. `AllowIfSafe` + write/DDL attempts:
   - Insert/update/delete into a collection that contains plugin-owned indexes throws `PLUGIN_REQUIRED`.
   - Ensure/drop index, drop/rename collection on an affected collection throws `PLUGIN_REQUIRED`.
10. Edge case (optional): `IndexType != 0` with missing/corrupt plugin metadata
   - Craft a database where a collection has a non-btree index but the plugin metadata entry is missing/corrupt; assert enforcement still triggers and diagnostics use the `<unknown>:type:{indexType}` warning key.

## Validation-on-open (opt-in)

11. Enable `ValidatePluginsOnOpen`, open db without plugin and with strict policy; expect failure at construction time (fail-fast), including for databases created before this feature (no header marker dependency).
12. Enable `ValidatePluginsOnOpen` with `AllowIfSafe`, open db without plugin; expect warnings but construction succeeds.

## $plugins

13. With plugin present: `$plugins` lists pluginId, loaded=true, correct collections/indexCount.
14. Without plugin and strict policy: `$plugins` still lists pluginId, loaded=false (introspection must work without triggering missing-plugin enforcement).

## Rebuild drop option

15. Seed file db with vector index:

- `db.Rebuild()` without plugin throws (existing tests should still pass).
- `db.Rebuild(new RebuildOptions { DropOrphanedPluginIndexes = true })` succeeds without plugin.
- Assert the dropped plugin index is absent after rebuild (no row for that index name) and re-creating it returns `true` (proves it was dropped).
- Assert `_rebuild_errors` (or equivalent rebuild report) contains an entry indicating the plugin index was dropped.
- Reopen with plugin and explicitly re-create vector index; ensure it works.

## Run verification

- `dotnet test LiteDB.sln --settings tests.runsettings`
- Optional full matrix: `pwsh -File scripts/run-tests-per-target.ps1`
