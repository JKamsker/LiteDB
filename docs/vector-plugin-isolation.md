# Migration Guide: Vector Plugin Isolation

LiteDB 6.0 separates all vector search capabilities (BSON types, index metadata, planner hooks, diagnostics) into the optional `LiteDB.Vector` plugin. The core `LiteDB` package now exposes only plugin-agnostic extension points. This guide explains how to keep applications working, migrate prerelease databases, and verify the new boundaries.

## Who should read this
- Application teams consuming LiteDB 6.0+ that previously referenced vector APIs from the core assembly.
- Plugin authors implementing vector or custom index functionality via the new registries.
- Release managers validating that repositories and CI pipelines stop depending on core vector symbols.

## Key changes
1. `LiteDB` no longer exposes `Vector*` types, helpers, or diagnostics. Vector indexing lives entirely in `LiteDB.Vector`.
2. Core detects plugin-owned assets (vector indexes, vector BSON payloads, reserved page codes) and surfaces a single warning plus `LITE2002 VectorCompatibility.PluginRequired` when the plugin is missing.
3. Registries (`ICustomIndexStrategyRegistry`, `IPluginIndexMetadataRegistry`, `ICustomBsonTypeRegistry`, page factories, query operator/cost registries) are the only supported integration points for plugins.
4. Build and CI gates must fail if source projects retain direct `"Vector"` references outside extension points (see `scripts/verify-vector-clean.ps1` once added).

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
   - `rg "Vector" LiteDB` (should only match extension points and tests once the repo is clean).

## Handling prerelease vector databases
Prerelase builds created vector metadata formats that GA releases refuse to load automatically. Choose one of these migration paths before upgrading:

| Scenario | Steps | Notes |
|---------|-------|-------|
| **Recommended (export/import)** | 1. Export documents (JSON/BSON dump or app-level export).<br>2. Create a fresh database using GA LiteDB + `LiteDB.Vector` plugin.<br>3. Reinsert documents and recreate vector indexes through the plugin. | Gives full control over the new metadata layout and avoids touching legacy indexes. |
| **Drop & Recreate** | 1. Install the final prerelease build that still understands old metadata.<br>2. Drop every vector index via `LiteCollectionVectorExtensions.DropIndex`.<br>3. Upgrade to GA LiteDB + plugin.<br>4. Recreate indexes using GA vector options. | GA releases emit `LITE2002` until legacy indexes are removed. |

### Behavior without the plugin
- LiteDB opens the database, logs one warning about missing plugin registrations, and marks vector collections/indexes as unavailable.
- Any attempt to access plugin-owned assets throws `LiteException (LITE2002)` with `PluginId="LiteDB.Vector"` and diagnostic context.
- Non-vector collections remain fully readable and writable.

### Diagnostics expectations
- **Warning**: `Vector plugin missing; plugin-owned metadata detected (LiteDB.Vector).` (logged once per database session)
- **Exception**: `VectorCompatibility.PluginRequired` (`LITE2002`) when vector indexes/BSON types/pages are touched without the plugin.
- **Conflict detection**: During plugin initialization, duplicate BSON type codes (`0x90-0x9F`) or page codes (`0xE0-0xEF`) produce `InvalidOperationException` referencing both plugin IDs.

## CI & release validation
1. Restore and build the solution: `dotnet build LiteDB.sln -c Release`.
2. Execute the shared test suite with plugin optionality filters: `dotnet test LiteDB.sln --settings tests.runsettings`.
3. Run `dotnet test LiteDB.Vector.Tests` independently to cover plugin-owned behaviors.
4. (After T002) call `scripts/verify-vector-clean.ps1` in CI to gate accidental `"Vector"` references in core projects.
5. Update documentation (README, shell usage, samples) to mention that vector search now requires installing and registering `LiteDB.Vector`.

## Troubleshooting
- **Still seeing `Vector*` symbols after removing the plugin reference**: Clean and rebuild the solution; ensure no project maintains a direct reference to `LiteDB.Vector`.
- **`LITE2002` when plugin is installed**: Confirm `VectorSearchPlugin.Instance` is passed through `LiteDatabaseOptions.Plugins` and that only one plugin registers the reserved identifier ranges.
- **Need temporary access to drop legacy indexes**: Run the final prerelease build (see release notes) which keeps read-only compatibility solely for index removal operations; immediately upgrade after cleanup to avoid divergence.

For additional architectural details, see `specs/001-vector-plugin-extraction/plan.md` and `quickstart.md`.
