# Files summary (expected touch points)

## New

- `LiteDB/Client/Database/LiteDatabaseBuilder.cs`
- `LiteDB/Client/Database/EngineReuse.cs`
- `LiteDB/Client/Database/ILiteDatabaseFactory.cs`
- `LiteDB/Client/Database/LiteDatabaseFactory.cs`
- `LiteDB/Engine/SystemCollections/SysPlugins.cs` (or equivalent partial method)
- `LiteDB/Plugins/IPluginValidationState.cs` (internal helper interface)

## Modify

- `LiteDB/Client/Database/LiteDatabase.cs`
- `LiteDB/Client/Database/LiteDatabaseOptions.cs`
- `LiteDB/Plugins/DefaultPluginContext.cs` (store validation flags; host policy application)
- `LiteDB/Engine/Services/SnapShot.cs` (write-mode refusal under `AllowIfSafe`/`RefuseOperations`)
- `LiteDB/Engine/Query/QueryOptimization.cs` (ignore non-btree indexes)
- `LiteDB/Engine/EnginePragmas.cs` + `LiteDB/Engine/Pragmas.cs` (`HAS_PLUGIN_INDEXES`)
- `LiteDB/Engine/Engine/Index.cs` (set header marker for custom indexes)
- `LiteDB/Engine/SystemCollections/Register.cs` (register `$plugins`)
- `LiteDB/Engine/Structures/RebuildOptions.cs`
- `LiteDB/Engine/Engine/Rebuild.cs`
- `LiteDB/Engine/Services/RebuildService.cs`
- `LiteDB/Engine/FileReader/FileReaderV8.cs`
- (Optionally) update plugin docs to state missing-plugin policy is host-controlled.

