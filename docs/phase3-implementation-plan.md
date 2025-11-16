# Phase 3 Implementation Plan: Vector Plugin Extraction

## Overview

This document outlines the detailed steps needed to complete Phase 3 of the vector plugin extraction, which moves all vector-specific code from LiteDB core into a separate LiteDB.Vector plugin.

**Status**: Foundation complete (Phases 1-2). Ready to begin extraction.

**Estimated Files Affected**: 51 files with vector references

## Current State Analysis

### Files Requiring Changes (51 total)

#### Plugin Infrastructure (Keep - These are extension points)
- ✅ `LiteDB/Plugins/Bson/PluginBsonTypeRegistry.cs` - Reserved ranges for vector
- ✅ `LiteDB/Plugins/DefaultPluginContext.cs` - VectorIndexes registry (legacy, can be deprecated)
- ✅ `LiteDB/Plugins/ILitePlugin.cs` - IVectorIndexStrategyRegistry (legacy, can be deprecated)
- ✅ `LiteDB/Plugins/Indexing/VectorIndexStrategyDescriptor.cs` - Plugin infrastructure
- ✅ `LiteDB/Plugins/Indexing/VectorIndexMetadataSerializer.cs` - Plugin infrastructure
- ✅ `LiteDB/Plugins/Storage/PluginPageFactoryRegistry.cs` - Reserved ranges

#### Core Files To Clean (Move to LiteDB.Vector)

**Client Layer:**
- `LiteDB/Client/Database/Collections/Index.cs` - Remove VectorIndexOptions overloads
- `LiteDB/Client/Database/ILiteCollection.cs` - Remove vector EnsureIndex overloads
- `LiteDB/Client/Database/ILiteRepository.cs` - Remove vector EnsureIndex overloads
- `LiteDB/Client/Database/LiteRepository.cs` - Remove vector EnsureIndex implementations
- `LiteDB/Client/Database/VectorIndexOptions.cs` - **MOVE TO LiteDB.Vector**
- `LiteDB/Client/Shared/SharedEngine.cs` - Remove EnsureVectorIndex method

**Document Layer:**
- `LiteDB/Document/BsonType.cs` - Remove Vector enum value
- `LiteDB/Document/BsonValue.cs` - Remove vector value handling
- `LiteDB/Document/Expression/Methods/Vector.cs` - **MOVE TO LiteDB.Vector**
- `LiteDB/Document/Expression/Parser/BsonExpressionFunctions.cs` - Remove vector functions
- `LiteDB/Document/Expression/Parser/BsonExpressionOperators.cs` - Remove vector operators
- `LiteDB/Document/Expression/Parser/BsonExpressionParser.cs` - Remove vector parsing
- `LiteDB/Document/Expression/Parser/BsonExpressionType.cs` - Remove vector expression types
- `LiteDB/Document/Json/JsonWriter.cs` - Remove vector JSON serialization

**Engine Layer:**
- `LiteDB/Engine/Disk/Serializer/BufferReader.cs` - Remove vector serialization
- `LiteDB/Engine/Disk/Serializer/BufferWriter.cs` - Remove vector serialization
- `LiteDB/Engine/Engine/Delete.cs` - Remove vector-specific delete logic
- `LiteDB/Engine/Engine/Index.cs` - Remove vector index operations
- `LiteDB/Engine/Engine/Insert.cs` - Remove vector-specific insert logic
- `LiteDB/Engine/Engine/Rebuild.cs` - Remove vector rebuild logic
- `LiteDB/Engine/Engine/Update.cs` - Remove vector-specific update logic
- `LiteDB/Engine/Engine/Upsert.cs` - Remove vector-specific upsert logic
- `LiteDB/Engine/FileReader/FileReaderV8.cs` - Remove vector file reading
- `LiteDB/Engine/FileReader/IndexInfo.cs` - Remove vector index info
- `LiteDB/Engine/ILiteEngine.cs` - **Remove EnsureVectorIndex method**
- `LiteDB/Engine/Pages/BasePage.cs` - Remove vector page type
- `LiteDB/Engine/Pages/CollectionPage.cs` - Remove vector metadata slots
- `LiteDB/Engine/Pages/VectorIndexPage.cs` - **MOVE TO LiteDB.Vector**
- `LiteDB/Engine/Query/IndexQuery/VectorIndexQuery.cs` - **MOVE TO LiteDB.Vector**
- `LiteDB/Engine/Query/Query.cs` - Remove vector query methods
- `LiteDB/Engine/Query/QueryOptimization.cs` - Remove hardcoded vector planner logic
- `LiteDB/Engine/Services/SnapShot.cs` - Remove vector snapshot logic
- `LiteDB/Engine/Services/VectorIndexService.cs` - **MOVE TO LiteDB.Vector**
- `LiteDB/Engine/Structures/VectorIndexMetadata.cs` - **MOVE TO LiteDB.Vector**
- `LiteDB/Engine/Structures/VectorIndexNode.cs` - **MOVE TO LiteDB.Vector**

**Utility Layer:**
- `LiteDB/Utils/Extensions/BufferSliceExtensions.cs` - Remove vector extensions
- `LiteDB/Utils/Tokenizer.cs` - Remove vector tokens

## Implementation Strategy

### Step 1: Create LiteDB.Vector Plugin Project

**Action**: Create new class library project

```xml
<!-- LiteDB.Vector/LiteDB.Vector.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../LiteDB/LiteDB.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="System.Buffers" Version="4.5.1" />
  </ItemGroup>
</Project>
```

**Files to create:**
- `LiteDB.Vector/LiteDB.Vector.csproj`
- `LiteDB.Vector/VectorSearchPlugin.cs` - Main plugin implementation
- `LiteDB.Vector/Extensions/LiteCollectionVectorExtensions.cs` - Extension methods for ILiteCollection

### Step 2: Move Vector-Specific Types

**Priority Order**:
1. Move data structures (VectorIndexOptions, VectorIndexMetadata, VectorIndexNode)
2. Move page types (VectorIndexPage)
3. Move services (VectorIndexService)
4. Move query types (VectorIndexQuery)
5. Move expression methods (Vector.cs)

**For each type:**
1. Copy to LiteDB.Vector with namespace `LiteDB.Vector`
2. Update access modifiers as needed (internal LiteDB types may need to become public in plugin)
3. Remove from LiteDB core
4. Update references

### Step 3: Create Extension Methods

Replace core API methods with extension methods in LiteDB.Vector:

```csharp
namespace LiteDB.Vector
{
    public static class LiteCollectionVectorExtensions
    {
        public static bool EnsureIndex<T>(
            this ILiteCollection<T> collection,
            string name,
            BsonExpression expression,
            VectorIndexOptions options)
        {
            // Implementation using plugin registries
            var context = GetPluginContext(collection);
            var strategy = context.Indexes.GetByKind("vector.hnsw");
            return strategy.EnsureIndex(...);
        }

        public static bool EnsureIndex<T, K>(
            this ILiteCollection<T> collection,
            Expression<Func<T, K>> keySelector,
            VectorIndexOptions options)
        {
            var expression = BsonExpression.Create(keySelector);
            return collection.EnsureIndex("$" + expression.Source, expression, options);
        }

        // Additional overloads...
    }
}
```

### Step 4: Implement Plugin Registration

```csharp
namespace LiteDB.Vector
{
    public sealed class VectorSearchPlugin : ILitePlugin
    {
        public static readonly VectorSearchPlugin Instance = new VectorSearchPlugin();

        private VectorSearchPlugin() { }

        public void Initialize(LiteDatabase database, ILitePluginContext context)
        {
            // Register BSON type
            context.RegisterBsonType(new BsonTypeRegistration(
                pluginId: "LiteDB.Vector",
                typeCode: 0x90,
                name: "Vector",
                serializer: VectorBsonSerializer.Write,
                deserializer: VectorBsonSerializer.Read,
                jsonFormatter: VectorBsonSerializer.ToJson
            ));

            // Register index metadata serializer
            context.IndexMetadata.Register(new PluginIndexMetadataDescriptor(
                pluginId: "LiteDB.Vector",
                indexKind: "vector.hnsw",
                serialize: VectorIndexMetadataSerializer.Serialize,
                deserialize: VectorIndexMetadataSerializer.Deserialize
            ));

            // Register page factory
            context.RegisterPageFactory(new PageFactoryRegistration(
                pluginId: "LiteDB.Vector",
                pageType: "VectorIndex",
                numericCode: 0xE0,
                compatibilityRange: ">=8.0",
                factory: ctx => ctx.IsNewPage
                    ? new VectorIndexPage(ctx.Buffer, ctx.PageId)
                    : new VectorIndexPage(ctx.Buffer)
            ));

            // Register index strategy
            var strategy = new VectorIndexStrategy(context);
            context.Indexes.Register(strategy);

            // Register SQL functions
            context.SqlFunctions.Register(new SqlFunctionRegistration(
                pluginId: "LiteDB.Vector",
                functionName: "VECTOR_DIST",
                implementation: VectorFunctions.Distance,
                minParameterCount: 2,
                maxParameterCount: 3
            ));

            context.SqlFunctions.Register(new SqlFunctionRegistration(
                pluginId: "LiteDB.Vector",
                functionName: "VECTOR_SIM",
                implementation: VectorFunctions.Similarity,
                minParameterCount: 2,
                maxParameterCount: 3
            ));

            // Register query operators
            context.QueryOperators.Register(new QueryOperatorRegistration(
                pluginId: "LiteDB.Vector",
                operatorName: "VECTOR_KNN",
                expressionType: BsonExpressionType.VectorKnn,
                parser: VectorExpressionParser.ParseKnn
            ));

            // Register query planner rule
            context.QueryPlanner.AddRule(new VectorIndexPlanningRule(), order: 100);

            // Register cost model
            context.QueryCostModels.Register(new QueryCostModelRegistration(
                pluginId: "LiteDB.Vector",
                indexKind: "vector.hnsw",
                calculateCost: VectorCostModel.Calculate
            ));
        }
    }
}
```

### Step 5: Remove Core Vector APIs

**ILiteEngine.cs**:
```csharp
// REMOVE:
bool EnsureVectorIndex(string collection, string name, BsonExpression expression, VectorIndexOptions options);
```

**SharedEngine.cs**:
```csharp
// REMOVE:
public bool EnsureVectorIndex(string collection, string name, BsonExpression expression, VectorIndexOptions options)
{
    return QueryDatabase(() => _engine.EnsureVectorIndex(collection, name, expression, options));
}
```

**ILiteCollection.cs**:
```csharp
// REMOVE all VectorIndexOptions overloads:
bool EnsureIndex(string name, BsonExpression expression, VectorIndexOptions options);
bool EnsureIndex(BsonExpression expression, VectorIndexOptions options);
bool EnsureIndex<K>(Expression<Func<T, K>> keySelector, VectorIndexOptions options);
bool EnsureIndex<K>(string name, Expression<Func<T, K>> keySelector, VectorIndexOptions options);
```

### Step 6: Update Core for Plugin Support

**CollectionPage.cs** - Replace vector metadata slots:
```csharp
// OLD:
private byte[] _vectorMetadata;

// NEW (use generic plugin metadata):
private Dictionary<string, byte[]> _pluginMetadata;

// Serialization format: {pluginIdLength:byte}{pluginId:utf8}{payloadLength:ushort}{payload:bytes}
```

**BsonType enum** - Remove Vector:
```csharp
public enum BsonType : byte
{
    // ... existing types ...
    // REMOVE: Vector = 0x90,  // Now plugin-owned
}
```

**BsonValue.cs** - Remove vector handling:
```csharp
// REMOVE IsVector property and vector-specific methods
// Vector BSON values now handled by plugin
```

**FileReaderV8.cs** - Use plugin registry for metadata:
```csharp
// OLD: Hardcoded vector metadata reading
if (indexType == IndexType.Vector)
{
    var metadata = VectorIndexMetadataSerializer.Read(reader);
    // ...
}

// NEW: Use plugin registry
if (context.IndexMetadata.TryGet(indexKind, out var descriptor))
{
    var metadata = descriptor.Deserialize(payloadBytes);
}
else
{
    // Emit LITE2002: Plugin required
    context.Logger.Write(LogLevel.Warning,
        $"Index '{indexName}' requires plugin for kind '{indexKind}' but plugin not registered.");
}
```

### Step 7: Create VectorCompatibility Class

**LiteDB.Vector/Utils/VectorCompatibility.cs**:
```csharp
namespace LiteDB.Vector
{
    public static class VectorCompatibility
    {
        public static LiteException PluginRequired(string operation, BsonDocument diagnostics = null)
        {
            var message = $"LiteDB.Vector plugin is required for operation '{operation}'. " +
                         "Install the LiteDB.Vector package and register VectorSearchPlugin during database initialization.";

            var exception = new LiteException(2002, message)
            {
                Data =
                {
                    ["PluginId"] = "LiteDB.Vector",
                    ["Operation"] = operation,
                    ["ErrorCode"] = "LITE2002"
                }
            };

            if (diagnostics != null)
            {
                exception.Data["Diagnostics"] = diagnostics;
            }

            return exception;
        }

        public static LiteException LegacyIndexNeedsRebuild(string indexName, string collection)
        {
            var message = $"Vector index '{indexName}' in collection '{collection}' uses a prerelease format. " +
                         "Drop and recreate the index using the GA release.";

            return new LiteException(2002, message)
            {
                Data =
                {
                    ["PluginId"] = "LiteDB.Vector",
                    ["IndexName"] = indexName,
                    ["Collection"] = collection,
                    ["ErrorCode"] = "LITE2002",
                    ["Remediation"] = "Drop the index using the final prerelease build, upgrade to GA, and recreate."
                }
            };
        }
    }
}
```

### Step 8: Add Tests

**LiteDB.Tests/Client/VectorOptionalityTests.cs**:
```csharp
public class VectorOptionalityTests
{
    [Fact]
    public void Core_LiteDB_Should_Not_Expose_Vector_Types()
    {
        // Use reflection to verify no Vector* types in LiteDB assembly
        var liteDbAssembly = typeof(LiteDatabase).Assembly;
        var vectorTypes = liteDbAssembly.GetTypes()
            .Where(t => t.Name.Contains("Vector", StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Namespace.Contains("Plugins")) // Extension points allowed
            .ToList();

        vectorTypes.Should().BeEmpty(
            "Core LiteDB should not expose vector types outside plugin extension points");
    }

    [Fact]
    public void ILiteCollection_Should_Not_Have_Vector_Methods()
    {
        var collectionType = typeof(ILiteCollection<>);
        var methods = collectionType.GetMethods()
            .Where(m => m.GetParameters().Any(p => p.ParameterType.Name.Contains("Vector")))
            .ToList();

        methods.Should().BeEmpty(
            "ILiteCollection should not have methods accepting VectorIndexOptions");
    }
}
```

**LiteDB.Vector.Tests/Integration/PluginRegistrationTests.cs**:
```csharp
public class PluginRegistrationTests
{
    [Fact]
    public void Vector_Operations_Should_Work_With_Plugin_Registered()
    {
        var options = new LiteDatabaseOptions
        {
            Plugins = new[] { VectorSearchPlugin.Instance }
        };

        using var db = new LiteDatabase(":memory:", options);
        var collection = db.GetCollection<Document>("docs");

        // Should work - plugin registered
        var result = collection.EnsureIndex(
            x => x.Embedding,
            new VectorIndexOptions(384, VectorDistanceMetric.Cosine));

        result.Should().BeTrue();
    }

    [Fact]
    public void Vector_Operations_Should_Fail_Without_Plugin()
    {
        using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection<Document>("docs");

        // Should fail - plugin not registered
        Action act = () => collection.EnsureIndex(
            x => x.Embedding,
            new VectorIndexOptions(384));

        act.Should().Throw<LiteException>()
            .Which.ErrorCode.Should().Be(2002);
    }
}
```

## Execution Checklist

### Phase 3A: Project Setup
- [ ] Create LiteDB.Vector project
- [ ] Add to solution
- [ ] Configure project references
- [ ] Create directory structure

### Phase 3B: Move Core Types (In Order)
- [ ] Move VectorIndexOptions.cs
- [ ] Move VectorIndexMetadata.cs
- [ ] Move VectorIndexNode.cs
- [ ] Move VectorIndexPage.cs
- [ ] Move VectorIndexService.cs
- [ ] Move VectorIndexQuery.cs
- [ ] Move Vector expression methods

### Phase 3C: Create Plugin Infrastructure
- [ ] Create VectorSearchPlugin.cs
- [ ] Implement Initialize method with all registrations
- [ ] Create VectorBsonSerializer
- [ ] Create VectorIndexMetadataSerializer (refactor from existing)
- [ ] Create VectorIndexStrategy
- [ ] Create VectorIndexPlanningRule
- [ ] Create VectorCostModel

### Phase 3D: Create Extension Methods
- [ ] Create LiteCollectionVectorExtensions
- [ ] Implement all EnsureIndex overloads
- [ ] Create VectorQueryExtensions (if needed)
- [ ] Create helper methods

### Phase 3E: Remove Core APIs
- [ ] Remove from ILiteEngine
- [ ] Remove from SharedEngine
- [ ] Remove from ILiteCollection
- [ ] Remove from ILiteRepository
- [ ] Remove from LiteRepository
- [ ] Remove from Collections/Index.cs

### Phase 3F: Update Core for Plugins
- [ ] Update CollectionPage to use generic plugin metadata
- [ ] Remove Vector from BsonType enum
- [ ] Update BsonValue to delegate to plugin
- [ ] Update FileReaderV8 to use plugin registry
- [ ] Update QueryOptimization to consult cost registry
- [ ] Update expression parser to consult operator registry
- [ ] Update Rebuild.cs to use plugin serializers

### Phase 3G: Testing
- [ ] Add VectorOptionalityTests
- [ ] Add PluginRegistrationTests
- [ ] Add BehaviorMatrixIntegrationTests
- [ ] Add VectorMetadataCompatibilityTests
- [ ] Run full test suite
- [ ] Verify builds succeed
- [ ] Run verification script

### Phase 3H: Documentation
- [ ] Update README with plugin usage
- [ ] Update migration guide with examples
- [ ] Document breaking changes
- [ ] Create quickstart guide
- [ ] Add API documentation

## Success Criteria

- ✅ Core LiteDB builds without LiteDB.Vector reference
- ✅ No `Vector*` types exposed from core LiteDB (verified via reflection)
- ✅ All tests pass with plugin registered
- ✅ Operations fail gracefully (LITE2002) without plugin
- ✅ Verification script reports no vector leakage
- ✅ Vector query performance ≤2% regression
- ✅ Migration paths documented and tested

## Risks and Mitigations

**Risk**: Breaking existing applications
**Mitigation**: Clear migration guide, compatibility warnings, phased rollout

**Risk**: Performance regression
**Mitigation**: Benchmark before/after, target ≤2% regression, optimize hotpaths

**Risk**: Complex plugin registration
**Mitigation**: Simple single-line registration (VectorSearchPlugin.Instance), clear examples

**Risk**: Missing edge cases
**Mitigation**: Comprehensive test matrix, behavior documentation, diagnostics

## Notes

- This is a breaking change for prerelease vector users
- GA release is first stable vector release with plugin architecture
- All vector functionality must work through plugin extensibility points
- No hardcoded vector logic should remain in core after Phase 3
