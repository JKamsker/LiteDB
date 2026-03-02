# Files summary (expected touch points)

## New

- [x] `LiteDB/Client/Database/LiteDatabaseBuilder.cs`
- [x] `LiteDB/Client/Database/ILiteDatabaseFactory.cs`
- [x] `LiteDB/Client/Database/LiteDatabaseFactory.cs`
- [x] `LiteDB/Engine/SystemCollections/SysPlugins.cs` (or equivalent partial method)
- [x] `LiteDB/Engine/Services/PluginRequirementScanner.cs` (internal shared scanner for typed API, `$plugins`, and validation-on-open)
- [x] `LiteDB/Plugins/IPluginValidationState.cs` (internal helper interface)

## Modify

- [x] `LiteDB/Client/Database/LiteDatabase.cs` (internal constructor for factory/lease support)
- [x] `LiteDB/Client/Database/LiteDatabaseOptions.cs` (add `MissingPluginBehavior`, `ValidatePluginsOnOpen`)
- [x] `LiteDB/Client/Shared/SharedEngine.cs` (exception safety for `SetPluginContext`: dispose/null engine on failure; `_transactionRunning` thread-safety under factory sharing)
- [x] `LiteDB/Plugins/DefaultPluginContext.cs` (store validation flags; host policy propagation; `Freeze()` method; `DiagnosticPolicy` setter frozen-check)
- [x] `LiteDB/Plugins/ILitePlugin.cs` (resolve `Initialize` signature -- option a: `Initialize(ILitePluginContext)` or option b: add `OnHandleCreated`)
- [x] `LiteDB/Plugins/PluginDiagnosticPolicy.cs` (remove hard-coded Vector message; deprecate `MissingBehavior` property)
- [x] `LiteDB/Engine/Services/SnapShot.cs` (write-mode refusal under `AllowIfSafe`; scan `IndexType != 0` in `EvaluatePluginAssets`; db-scoped warn cache; raw page-buffer helper for `$plugins`/validation scans; `DropCollection` defensive guard)
- [x] `LiteDB/Engine/Query/QueryOptimization.cs` (filter `IndexType == 0` in `ChooseIndex` -- **existing bug fix**)
- [x] `LiteDB/Engine/SystemCollections/Register.cs` (register `$plugins`)
- [x] `LiteDB/Engine/Structures/RebuildOptions.cs` (add `DropOrphanedPluginIndexes`)
- [x] `LiteDB/Engine/Engine/Rebuild.cs` (preflight always runs regardless of mode; `TryRebuildPluginIndex` checks `IndexType != 0`)
- [x] `LiteDB/Engine/Services/RebuildService.cs` (pass `DropOrphanedPluginIndexes` to `FileReaderV8`)
- [x] `LiteDB/Engine/FileReader/FileReaderV8.cs` (don't swallow `PLUGIN_REQUIRED` in `Open()` catch-all; handle `IndexType != 0` without metadata in `LoadIndexes`)
- [x] `LiteDB/Engine/Pages/CollectionPage.cs` (add a fault-tolerant scan helper for `$plugins`/validation that does not throw on legacy/corrupt metadata and does not compile `BsonExpression`)
- [x] `LiteDB/Engine/LiteEngine.cs` (`SetPluginContext` validation-on-open logic; document `_plugins` field memory ordering requirement)

## External plugin modifications

- [x] `LiteDB.Spatial/Plugin/SpatialPlugin.cs` + `SpatialPluginRegistry.cs` + `SpatialPluginServices.cs` (remove `LiteDatabase` capture from `Initialize` path; adapt to new `Initialize` signature)

## Removed files

- [x] `LiteDB/Client/Database/FactoryReuse.cs` (eliminated; `Build()` vs `BuildFactory()` replaces the enum)
