# Test plan (new/updated coverage)

## Builder / Factory

1. Builder smoke: build `:memory:` db with vector plugin; create vector index; query.
2. Factory (`EngineReuse.None`):
   - Create factory for `:memory:`; create db1/db2; assert isolated contexts (e.g., plugin registrations don’t leak if you use per-build plugin factories).
3. Factory (`EngineReuse.Shared`):
   - Create factory with shared engine; `CreateDatabase()` twice.
   - Verify plugin initialization ran once (use a tracking plugin).
   - Verify disposing db wrappers does not dispose engine until factory disposed (or until factory disposed + refcount==0, per implementation).
4. Dispose ordering:
   - Dispose factory first, then dispose db wrappers; ensure no crashes/leaks and engine disposed exactly once at the right time.

## Missing-plugin behavior (host-controlled)

5. Default (`RefuseDatabase`) unchanged: open db with plugin index but without plugin; accessing that collection throws `PLUGIN_REQUIRED`.
6. `AllowIfSafe` + read snapshot:
   - Open db without plugin but with `AllowIfSafe`; verify reading a non-plugin query plan doesn’t attempt plugin indexes (after planner fix).
7. `AllowIfSafe` + write attempt:
   - Insert/update/delete into a collection that contains plugin-owned indexes throws `PLUGIN_REQUIRED`.

## Header marker + validation-on-open

8. Create plugin index, verify `Pragmas.HAS_PLUGIN_INDEXES == true`.
9. Enable `ValidatePluginsOnOpen`, open db without plugin and with strict policy; expect failure at construction time (fail-fast) once marker is set.

## $plugins

10. With plugin present: `$plugins` lists pluginId, loaded=true, correct collections/indexCount.
11. Without plugin and strict policy: `$plugins` still lists pluginId, loaded=false (because it bypasses validation checks while scanning).

## Rebuild drop option

12. Seed file db with vector index:

- `db.Rebuild()` without plugin throws (existing tests should still pass).
- `db.Rebuild(new RebuildOptions { DropOrphanedPluginIndexes = true })` succeeds without plugin.
- Reopen with plugin and explicitly re-create vector index; ensure it works.

## Run verification

- `dotnet test LiteDB.sln --settings tests.runsettings`
- Optional full matrix: `pwsh -File scripts/run-tests-per-target.ps1`

