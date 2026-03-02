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
       var pluginId = VectorPlugin.PluginId;

       context.SetDiagnosticPolicy(VectorPluginDiagnosticPolicy.Instance);

       context.RegisterBsonType(VectorBsonSerializer.CreateDescriptor(pluginId));
       context.RegisterSqlFunction(VectorSqlFunctions.CreateVectorDistance(pluginId));
       context.RegisterSqlFunction(VectorSqlFunctions.CreateVectorSimilarity(pluginId));
       context.RegisterQueryOperator(VectorQueryOperators.CreateVectorKnn(pluginId));
       context.RegisterQueryCostModel(VectorQueryCostModel.Create(pluginId));

       context.RegisterIndexMetadata(new PluginIndexMetadataDescriptor(
           pluginId: pluginId,
           indexKind: VectorCompatibility.DefaultIndexKind,
           serialize: VectorMetadataSerializer.Serialize,
           deserialize: VectorMetadataSerializer.Deserialize));

       context.Indexes.Register(new VectorIndexStrategy(context.Logger, defaultMetric: null));
       context.QueryPlanner.AddRule(new VectorIndexPlanningRule());

       context.RegisterPageFactory(new PageFactoryRegistration(
           pluginId: pluginId,
           pageType: "VectorIndex",
           numericCode: VectorPlugin.PageTypeCode,
           compatibilityRange: ">=8.0",
           factory: ctx =>
           {
               var buffer = (PageBuffer)ctx.Buffer;
               return ctx.IsNewPage ? new VectorIndexPage(buffer, ctx.PageId) : new VectorIndexPage(buffer);
           }));
   }
   ```
   These registrations ensure collection pages, BSON serialization, and page factories all route through the plugin; without them, behavior follows `PluginMissingBehavior` (default strict): warnings are emitted only in non-strict modes and `LiteException (LITE2002 / PLUGIN_REQUIRED)` is thrown when vector-owned assets are accessed.
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
   Depending on `PluginMissingBehavior` (default strict), LiteDB may log a warning (non-strict modes) and will throw `LITE2002` when vector-owned assets are accessed; non-vector collections remain usable.
5. **Create vector indexes via extensions** (no core APIs expose vector methods):
   ```csharp
   var options = new VectorIndexOptions(dimensions: 384);
   db.GetCollection<MyDoc>("docs").EnsureIndex(x => x.Embedding, options);
   ```
6. **Optional: inspect raw metadata from inside the plugin**—deserialize the payload using your registered descriptor:
   ```csharp
   var metadata = VectorMetadataSerializer.Deserialize(payloadBytes); // returns a BsonDocument
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

