# Quickstart – Vector Plugin Isolation

1. **Install packages**
   ```bash
   dotnet add package LiteDB
   dotnet add package LiteDB.Vector
   ```
   **Breaking change (prerelease-only)**: Databases created with prerelease vector builds will no longer run vector operations without the plugin. When LiteDB detects plugin-owned assets but no plugin, it emits `LITE2002` (missing `LiteDB.Vector`) and leaves the rest of the data fully accessible.

   **Migration paths**:

   - **Recommended**: Perform a logical export (documents only), create a fresh database with GA release + LiteDB.Vector plugin, reinsert documents, then re-run vector `EnsureIndex` calls.
   - **Alternative**: Use the final prerelease build to drop existing vector indexes (prerelease builds retain read-only access to old metadata format for drop operations), upgrade to GA release, install LiteDB.Vector plugin, and recreate the indexes. Note: GA releases cannot read prerelease formats and will emit `LITE2002` until indexes are dropped and recreated.
2. **Register the plugin** when constructing the database. Existing constructors remain available; this overload keeps things non-breaking:
   ```csharp
   var options = new LiteDatabaseOptions
   {
       Plugins = new ILitePlugin[] { VectorSearchPlugin.Instance }
   };

   using var db = new LiteDatabase(connectionString, options: options);
   ```
3. **(For plugin authors) Register extension points inside `Initialize`**—LiteDB.Vector does this for you, but custom plugins follow the same pattern:
   ```csharp
   public void Initialize(LiteDatabase database, ILitePluginContext context)
   {
       context.RegisterBsonType(new CustomBsonTypeDescriptor(
           pluginId: "LiteDB.Vector",
           typeCode: 0x90,
           name: "Vector",
           serializer: VectorBsonSerializer.Write,
           deserializer: VectorBsonSerializer.Read));

       context.IndexMetadata.Register(new PluginIndexMetadataDescriptor(
           pluginId: "LiteDB.Vector",
           indexKind: "vector.hnsw",
           serialize: VectorIndexMetadataSerializer.Serialize,
           deserialize: VectorIndexMetadataSerializer.Deserialize));

       context.RegisterPageFactory(new PageFactoryRegistration(
           pluginId: "LiteDB.Vector",
           pageType: "VectorIndex",
           numericCode: 0xE0,
           compatibilityRange: ">=8.0",
           factory: c => c switch
           {
               PageConstructionContext p when p.IsNewPage => new VectorIndexPage(p.Buffer, p.PageId),
               PageConstructionContext p => new VectorIndexPage(p.Buffer),
               _ => throw new ArgumentException("Unexpected context", nameof(c))
           }));

       // finally register strategies/operators as usual
       context.Indexes.Register(VectorIndexStrategy.Create(context));
        context.QueryPlanner.AddRule(new VectorIndexPlanningRule());
        context.QueryOperators.Register(FunctionRegistration.VectorDist());
        context.QueryCostModel.Register(VectorCostModel.Instance);
   }
   ```
   These registrations ensure collection pages, BSON serialization, and page factories all route through the plugin; without them, LiteDB will warn once on open and throw a `VectorCompatibility.PluginRequired` (`LITE2002`) exception only when vector assets are accessed.
4. **Demonstrate missing-plugin behavior**—if you forget to register the plugin, vector operations fail deterministically while other data stays accessible:
   ```csharp
   using var db = new LiteDatabase(connectionString); // no plugins
   var docs = db.GetCollection<MyDoc>("docs");

   try
   {
       docs.EnsureIndex(x => x.Embedding, new VectorIndexOptions(384));
   }
   catch (LiteException ex) when (ex.Message.Contains("VectorSearchPlugin"))
   {
       Console.WriteLine($"Vector plugin required (LITE2002): {ex.Message}");
       if (ex.Data["VectorDiagnostics"] is BsonDocument diagnostics)
       {
           Console.WriteLine(diagnostics.ToString());
       }
   }
   ```
   You will see a single warning in the logs about plugin-owned metadata being detected; all non-vector collections remain fully writable.
5. **Create vector indexes via extensions** (no core APIs expose vector methods):
   ```csharp
   var options = new VectorIndexOptions(dimensions: 384);
   db.GetCollection<MyDoc>("docs").EnsureIndex(x => x.Embedding, options);
   ```
6. **Optional: inspect raw metadata from inside the plugin**-for example, `VectorIndexStrategy` can deserialize payloads using the registered descriptor:
   ```csharp
   public sealed class VectorIndexStrategy : CustomIndexStrategy
   {
       public override PluginIndexMetadata DeserializeMetadata(byte[] payload)
       {
           var slot = payload[0];
           var dimensions = BitConverter.ToUInt16(payload, 1);
           var metric = payload[3];
            return new PluginIndexMetadata("vector.hnsw", new VectorMetadata(slot, dimensions, metric));
       }
   }
   ```
7. **Testing**: execute both `dotnet test LiteDB.Tests --filter Vector` and `dotnet test LiteDB.Vector.Tests` to validate optional-plugin behavior.
8. **Migration helper**: LiteDB.Vector can provide a convenience API so applications can rebuild prerelease indexes after installing the plugin:
   ```csharp
   public static class VectorMigrate
   {
       public static void RebuildAll(LiteDatabase db, CancellationToken ct = default)
       {
           // Note: Requires new ILiteEngine.GetIndexInfo() API to be added (see tasks.md Phase 6)
           // Alternative: use existing GetCollectionNames() + UserVersion metadata approach
           foreach (var info in db.GetIndexMetadata().Where(i => i.IndexKind == "vector.hnsw"))
           {
               var collection = db.GetCollection(info.Collection);
               collection.DropIndex(info.Name);

               // Re-create using GA plugin with original options
               var options = new VectorIndexOptions(
                   dimensions: (int)info.Metadata["dimensions"],
                   metric: (VectorMetric)info.Metadata["metric"]
               );
               collection.EnsureIndex(info.Name, info.Expression, options);
           }
       }
   }

   using var db = new LiteDatabase(connectionString, options: options);
   VectorMigrate.RebuildAll(db);
   ```

   **Implementation Note**: The exact API for enumerating indexes needs definition. Options include adding `ILiteEngine.GetIndexInfo()`, using collection introspection, or storing plugin metadata in UserVersion.

