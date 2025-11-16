# Phase 3F Detailed Implementation Plan

## Overview
Phase 3F involves updating the LiteDB core to use plugin registries for vector operations. This is complex work that touches core serialization, type systems, and query optimization.

## Current Status (After Phase 3E)

### ✅ Completed
1. **Phase 1-2**: All plugin registry infrastructure in place
   - IPluginIndexMetadataRegistry
   - ISqlFunctionRegistry
   - IQueryOperatorRegistry
   - IQueryCostModelRegistry
   - PluginBsonTypeRegistry
   - PluginPageFactoryRegistry

2. **Phase 3A-3D**: LiteDB.Vector plugin project created
   - VectorIndexOptions moved to plugin
   - VectorSearchPlugin skeleton
   - Extension methods for vector indexing
   - VectorCompatibility error utilities

3. **Phase 3E**: Public API cleaned
   - All VectorIndexOptions overloads removed from public interfaces
   - VectorIndexOptions made internal in core (temporary)
   - EnsureVectorIndex made private in engine (temporary)
   - No vector methods exposed in public API

### 🔄 Remaining Work for Phase 3F

## Task 1: BsonType and BsonValue Vector Handling

**Files to modify:**
- `LiteDB/Document/BsonType.cs` - Vector enum value
- `LiteDB/Document/BsonValue.cs` - Vector constructors, properties, operators
- `LiteDB/Document/BsonVector.cs` - Entire class
- `LiteDB/Document/Expression/Methods/Vector.cs` - Expression methods

**Approach:**
1. Add deprecation comments to BsonType.Vector
2. Keep BsonVector class but mark as internal/deprecated
3. Vector operations should check plugin registry
4. Add compatibility layer for existing databases

**Challenges:**
- BsonValue is used throughout the codebase
- Removing Vector support entirely breaks backward compatibility
- Need to support reading existing vector data while delegating operations to plugin

## Task 2: Buffer Serialization Layer

**Files to modify:**
- `LiteDB/Engine/Disk/Serializer/BufferReader.cs`
- `LiteDB/Engine/Disk/Serializer/BufferWriter.cs`
- `LiteDB/Utils/Extensions/BufferSliceExtensions.cs`

**Approach:**
1. When BufferReader encounters type code 100 (Vector):
   - Check if plugin is registered for that type code
   - If yes, delegate to plugin deserializer
   - If no, throw error with plugin required message

2. BufferWriter should:
   - Check plugin registry before writing custom types
   - Delegate serialization to plugin

**Implementation:**
```csharp
// In BufferReader
if (typeCode >= 100) // Custom/plugin types
{
    if (_pluginContext.BsonTypes.TryGet(typeCode, out var registration))
    {
        var bytes = this.ReadBytes();
        return registration.Deserialize(bytes);
    }
    else
    {
        throw VectorCompatibility.PluginRequired($"BsonType {typeCode}");
    }
}
```

## Task 3: CollectionPage Metadata Storage

**Files to modify:**
- `LiteDB/Engine/Pages/CollectionPage.cs`

**Current situation:**
- CollectionPage has hard-coded VectorIndexMetadata storage
- Methods like `GetVectorIndexMetadata()`, `InsertVectorIndex()`, etc.

**Approach:**
1. Replace specific vector metadata methods with generic plugin metadata storage
2. Store metadata as `Dictionary<string, byte[]>` where:
   - Key = index name
   - Value = serialized metadata from plugin

3. New methods:
```csharp
public byte[] GetPluginIndexMetadata(string indexName, string pluginId)
public void SetPluginIndexMetadata(string indexName, string pluginId, byte[] metadata)
public bool HasPluginMetadata(string indexName)
```

**Migration:**
- Existing databases with vector metadata need compatibility layer
- Read old format, convert to plugin format on first access

## Task 4: FileReader for Rebuild/Import

**Files to modify:**
- `LiteDB/Engine/FileReader/FileReaderV8.cs`
- `LiteDB/Engine/FileReader/IndexInfo.cs`

**Approach:**
1. FileReaderV8 should:
   - Read index metadata as raw bytes
   - Store in IndexInfo as opaque data
   - Let rebuild process delegate to plugin for deserialization

2. IndexInfo should have:
```csharp
public string PluginId { get; set; }
public byte[] PluginMetadata { get; set; }
```

## Task 5: Query Optimization

**Files to modify:**
- `LiteDB/Engine/Query/QueryOptimization.cs`
- `LiteDB/Engine/Query/IndexQuery/VectorIndexQuery.cs`

**Approach:**
1. QueryOptimization should:
   - Check IQueryCostModelRegistry for custom operators
   - Delegate cost calculation to plugins
   - Fall back to default costs for standard operations

2. VectorIndexQuery:
   - Should be moved to plugin or made generic
   - Plugin registers vector_distance operator with cost model

## Task 6: Rebuild Process

**Files to modify:**
- `LiteDB/Engine/Engine/Rebuild.cs`

**Current code (lines 81-88):**
```csharp
if (index.IndexType == 1 && index.VectorMetadata != null)
{
    this.EnsureVectorIndex(...);
}
```

**New approach:**
```csharp
if (index.PluginId != null && index.PluginMetadata != null)
{
    // Delegate to plugin to recreate index
    if (_pluginContext.IndexMetadata.TryGet(index.IndexKind, out var descriptor))
    {
        var metadata = descriptor.Deserialize(index.PluginMetadata);
        // Call plugin to recreate index
    }
    else
    {
        throw PluginRequired(index.PluginId, "Rebuild index");
    }
}
```

## Task 7: Vector-Specific Services

**Files to modify/remove:**
- `LiteDB/Engine/Services/VectorIndexService.cs` - Move to plugin
- `LiteDB/Engine/Query/IndexQuery/VectorIndexQuery.cs` - Move to plugin
- `LiteDB/Plugins/Indexing/VectorIndexMetadataSerializer.cs` - Already exists, enhance

**Approach:**
1. VectorIndexService should be in LiteDB.Vector plugin
2. Plugin registers it during initialization
3. Core engine accesses it through plugin registry

## Implementation Order

Recommended order to minimize breaking changes:

1. **Add deprecation comments** (low risk)
   - Mark all Vector code as deprecated
   - Add TODO comments

2. **Update BufferReader/Writer** (medium risk)
   - Add plugin registry checks
   - Keep backward compatibility

3. **Update CollectionPage** (high risk)
   - Most invasive change
   - Needs careful migration path

4. **Update FileReader** (medium risk)
   - Affects rebuild/import only
   - Can add compatibility layer

5. **Update Rebuild.cs** (medium risk)
   - Replace direct calls with plugin delegation
   - Keep internal methods for compatibility

6. **Update QueryOptimization** (medium risk)
   - Add plugin cost model consultation
   - Fall back to defaults

7. **Move VectorIndexService to plugin** (high risk)
   - Final step after all infrastructure ready
   - Requires plugin to be fully functional

## Testing Strategy

Each task should have:
1. Unit tests for plugin registry integration
2. Integration tests with plugin installed
3. Integration tests without plugin (should fail gracefully)
4. Migration tests for existing databases

## Risk Mitigation

1. **Backward Compatibility**: Keep reading old format data
2. **Graceful Degradation**: Clear errors when plugin missing
3. **Incremental Migration**: Don't force immediate migration
4. **Documentation**: Clear migration path for users

## Success Criteria

- ✅ Core LiteDB compiles without vector-specific public API
- ✅ LiteDB.Vector plugin can be optionally installed
- ✅ Existing databases with vector data readable (with plugin)
- ✅ New vector indexes only creatable through plugin
- ✅ Clear error messages when plugin required but missing
- ✅ All tests pass with and without plugin
