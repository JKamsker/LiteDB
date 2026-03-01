# Files summary (expected touch points)

## New

- `LiteDB/Client/Database/LiteDatabaseBuilder.cs`
- `LiteDB/Client/Database/ILiteDatabaseFactory.cs`
- `LiteDB/Client/Database/LiteDatabaseFactory.cs`
- `LiteDB/Engine/SystemCollections/SysPlugins.cs` (or equivalent partial method)
- `LiteDB/Engine/Services/PluginRequirementScanner.cs` (internal shared scanner for typed API, `$plugins`, and validation-on-open)
- `LiteDB/Plugins/IPluginValidationState.cs` (internal helper interface)

## Modify

- `LiteDB/Client/Database/LiteDatabase.cs` (internal constructor for factory/lease support)
- `LiteDB/Client/Database/LiteDatabaseOptions.cs` (add `MissingPluginBehavior`, `ValidatePluginsOnOpen`)
- `LiteDB/Client/Shared/SharedEngine.cs` (exception safety for `SetPluginContext`: dispose/null engine on failure; `_transactionRunning` thread-safety under factory sharing)
- `LiteDB/Plugins/DefaultPluginContext.cs` (store validation flags; host policy propagation; `Freeze()` method; `DiagnosticPolicy` setter frozen-check)
- `LiteDB/Plugins/ILitePlugin.cs` (resolve `Initialize` signature -- option a: `Initialize(ILitePluginContext)` or option b: add `OnHandleCreated`)
- `LiteDB/Plugins/PluginDiagnosticPolicy.cs` (remove hard-coded Vector message; deprecate `MissingBehavior` property)
- `LiteDB/Engine/Services/SnapShot.cs` (write-mode refusal under `AllowIfSafe`; scan `IndexType != 0` in `EvaluatePluginAssets`; db-scoped warn cache; raw page-buffer helper for `$plugins`/validation scans; `DropCollection` defensive guard)
- `LiteDB/Engine/Query/QueryOptimization.cs` (filter `IndexType == 0` in `ChooseIndex` -- **existing bug fix**)
- `LiteDB/Engine/SystemCollections/Register.cs` (register `$plugins`)
- `LiteDB/Engine/Structures/RebuildOptions.cs` (add `DropOrphanedPluginIndexes`)
- `LiteDB/Engine/Engine/Rebuild.cs` (preflight always runs regardless of mode; `TryRebuildPluginIndex` checks `IndexType != 0`)
- `LiteDB/Engine/Services/RebuildService.cs` (pass `DropOrphanedPluginIndexes` to `FileReaderV8`)
- `LiteDB/Engine/FileReader/FileReaderV8.cs` (don't swallow `PLUGIN_REQUIRED` in `Open()` catch-all; handle `IndexType != 0` without metadata in `LoadIndexes`)
- `LiteDB/Engine/Pages/CollectionPage.cs` (add a fault-tolerant scan helper for `$plugins`/validation that does not throw on legacy/corrupt metadata and does not compile `BsonExpression`)
- `LiteDB/Engine/LiteEngine.cs` (`SetPluginContext` validation-on-open logic; document `_plugins` field memory ordering requirement)

## External plugin modifications

- `LiteDB.Spatial/Plugin/SpatialPlugin.cs` + `SpatialPluginRegistry.cs` + `SpatialPluginServices.cs` (remove `LiteDatabase` capture from `Initialize` path; adapt to new `Initialize` signature)

## Removed files

- `LiteDB/Client/Database/FactoryReuse.cs` (eliminated; `Build()` vs `BuildFactory()` replaces the enum)
