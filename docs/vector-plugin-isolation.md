# Migration Guide: Vector Plugin Isolation

LiteDB 6.0 separates all vector search capabilities (BSON types, index metadata, planner hooks, diagnostics) into the optional `LiteDB.Vector` plugin. The core `LiteDB` package now exposes only plugin-agnostic extension points. This guide explains how to keep applications working, migrate prerelease databases, and verify the new boundaries.

## Who should read this
- Application teams consuming LiteDB 6.0+ that previously referenced vector APIs from the core assembly.
- Plugin authors implementing vector or custom index functionality via the new registries.
- Release managers validating that repositories and CI pipelines stop depending on core vector symbols.

## Key changes
1. `LiteDB` no longer exposes `Vector*` types, helpers, or diagnostics. Vector indexing lives entirely in `LiteDB.Vector`.
2. Core detects plugin-owned **indexes** via persisted collection metadata (`IndexType != 0` and/or plugin index metadata entries) and enforces missing-plugin behavior via host-controlled policy (`PluginMissingBehavior`, default strict). When the plugin is required but missing, operations throw `LiteException (LITE2002 / PLUGIN_REQUIRED)`.
3. Registries (`ICustomIndexStrategyRegistry`, `IPluginIndexMetadataRegistry`, `ICustomBsonTypeRegistry`, page factories, query operator/cost registries) are the only supported integration points for plugins.
4. Build and CI gates must fail if source projects retain direct `"Vector"` references outside extension points (see `scripts/verify-vector-clean.ps1`).

## Installation quickstart
1. Install the packages wherever you need vector search:
   ```bash
   dotnet add package LiteDB
   dotnet add package LiteDB.Vector
   ```
2. Register the plugin at database construction so LiteDB can load the BSON handler, metadata serializers, and query operators contributed by LiteDB.Vector:
   ```csharp
   var options = new LiteDatabaseOptions
   {
       Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
   };
   using var db = new LiteDatabase(connectionString, options: options);
   ```
3. Call vector helpers from the plugin (`LiteCollectionVectorExtensions.EnsureIndex`, `DropIndex`, vector query extensions, etc.). Without the plugin these methods are unavailable and vector operations throw `LiteException (LITE2002)`.
4. Need a deeper walkthrough? Follow `specs/001-vector-plugin-extraction/quickstart.md` end-to-end; it mirrors the public guidance we ship in the docs.

## Application migration checklist
1. **Add the plugin package**
   ```bash
   dotnet add package LiteDB.Vector
   ```
2. **Register the plugin on database construction**
   ```csharp
   var options = new LiteDatabaseOptions
   {
       Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
   };
   using var db = new LiteDatabase(connectionString, options: options);
   ```
   - You can keep existing constructors for non-vector scenarios; vector APIs simply will not be available without the plugin.
3. **Update vector index calls**
   - Use the extension methods in `LiteDB.Vector` (e.g., `LiteCollectionVectorExtensions.EnsureIndex(...)`). Core collection APIs remain unchanged for non-vector indexes.
4. **Run verification**
   - `dotnet test LiteDB.Tests --filter "FullyQualifiedName~Vector"`
   - `dotnet test LiteDB.Vector.Tests`
   - `pwsh -File scripts/verify-vector-api.ps1 -NoBuild` (after a build) to ensure LiteDB.dll does not expose new `Vector*` APIs
   - `rg "Vector" LiteDB` (should only match extension points and tests once the repo is clean).

## Compatibility matrix

| Database contents | Plugin registered? | Behavior in LiteDB 6.0+ | Required action |
|-------------------|--------------------|--------------------------|-----------------|
| No vector data present | Optional | Normal LiteDB behavior; plugin hooks stay idle even if loaded. | None. |
| GA vector indexes/pages (`LiteDB.Vector` metadata) | Yes | Full vector functionality available via plugin registries. | Keep plugin registered; rerun vector tests after upgrades. |
| GA vector indexes/pages (`LiteDB.Vector` metadata) | No | Behavior depends on `PluginMissingBehavior` (default strict): affected collections/indexes require the plugin and throw `LiteException (LITE2002 / PLUGIN_REQUIRED)` when accessed; unaffected collections remain usable. | Install `LiteDB.Vector`, register it via `LiteDatabaseOptions.Plugins`, and rerun the operation. |
| Prerelease vector artifacts (legacy metadata/page codes) | Yes or No | GA builds cannot deserialize the prerelease payloads; LiteDB raises `LITE2002` even if the plugin is installed. | Follow the breaking-change process below (export/import or drop & rebuild). |

## Breaking change: prerelease vector databases
Prerelease builds created vector metadata formats that GA releases refuse to load automatically. This is an intentional breaking change to keep GA builds simple and deterministic. Choose one of these migration paths before upgrading:

| Scenario | Steps | Notes |
|---------|-------|-------|
| **Recommended (export/import)** | 1. Export documents (JSON/BSON dump or app-level export).<br>2. Create a fresh database using GA LiteDB + `LiteDB.Vector` plugin.<br>3. Reinsert documents and recreate vector indexes through the plugin. | Gives full control over the new metadata layout and avoids touching legacy indexes. |
| **Drop & Recreate** | 1. Install the final prerelease build that still understands old metadata.<br>2. Drop every vector index via `LiteCollectionVectorExtensions.DropIndex`.<br>3. Upgrade to GA LiteDB + plugin.<br>4. Recreate indexes using GA vector options. | GA releases emit `LITE2002` until legacy indexes are removed. |

## Quickstart verification (2025-11-17)

We validated the public quickstart with a temporary console application that references the in-repo `LiteDB` and `LiteDB.Vector` projects. The sample registers the plugin via `LiteDatabaseOptions` (`new LiteDatabase(connectionString, options: options)`), matching the public guidance. The console project itself is not checked in, but the captured output is preserved in `artifacts_temp/vector-quickstart/run.log`.

Command output (trimmed to the quickstart steps):

| Step | Action | Observed result |
|------|--------|-----------------|
| 1 | Create the database with the plugin, insert three documents, and ensure a vector index | `documents=3 index_created=True` |
| 2 | Remove the plugin and try to add another vector index | `caught: 2002 - Vector index support requires the VectorSearchPlugin...` (`LITE2002 / PLUGIN_REQUIRED`) |
| 3 | Re-open with the plugin and drop the existing vector index (prerelease drop path) | `drop_index=True` |
| 4 | Open the cleaned database without the plugin and perform a normal insert | `docs_after_insert=4 (previous=3)` — non-vector operations continue to work |
| 5 | Export the documents with the plugin, create a new database, reinsert them, and recreate the vector index | `exported=4` / `imported=4 index_recreated=True` — matches the recommended export/import migration path |

These steps confirm the installation guidance, the missing-plugin diagnostic, and both migration paths documented above: drop & recreate (Steps 3–4) and export/import (Step 5).

### Behavior without the plugin
- LiteDB opens the database; access to vector collections/indexes is controlled by host policy (`PluginMissingBehavior`, default strict).
- Any attempt to access vector-owned indexes without the plugin throws `LiteException (LITE2002)` with `PluginId="LiteDB.Vector"` and diagnostic context.
- Non-vector collections remain readable and writable.

### Diagnostics expectations
- **Warning** (non-strict policies only): logged once per database identity when plugin-owned indexes are detected but the host allows the database to open.
- **Exception**: `LiteException (LITE2002 / PLUGIN_REQUIRED)` when plugin-owned indexes are touched without the plugin.
- **Conflict detection**: During plugin initialization, duplicate BSON type codes (`0x90-0x9F`) or page codes (`0xE0-0xEF`) produce `InvalidOperationException` referencing both plugin IDs.

## CI & release validation
1. Restore and build the solution: `dotnet build LiteDB.sln -c Release`.
2. Execute the shared test suite with plugin optionality filters: `dotnet test LiteDB.sln --settings tests.runsettings`.
3. Run `dotnet test LiteDB.Vector.Tests` independently to cover plugin-owned behaviors.
4. (After T002) call `scripts/verify-vector-clean.ps1` in CI to gate accidental `"Vector"` references in core projects.
5. Execute `pwsh -File scripts/verify-vector-api.ps1 -NoBuild` against the built artifacts to fail the build if new `Vector*` symbols leak from LiteDB core.
6. Update documentation (README, shell usage, samples) to mention that vector search now requires installing and registering `LiteDB.Vector`.

## Troubleshooting
- **Still seeing `Vector*` symbols after removing the plugin reference**: Clean and rebuild the solution; ensure no project maintains a direct reference to `LiteDB.Vector`.
- **`LITE2002` when plugin is installed**: Confirm `VectorSearchPlugin.Instance` is passed through `LiteDatabaseOptions.Plugins` and that only one plugin registers the reserved identifier ranges.
- **Need temporary access to drop legacy indexes**: Run the final prerelease build (see release notes) which keeps read-only compatibility solely for index removal operations; immediately upgrade after cleanup to avoid divergence.

For additional architectural details, see `specs/001-vector-plugin-extraction/plan.md` and `quickstart.md`.
