# Quickstart – Vector Plugin Isolation

1. **Install packages**
   ```bash
   dotnet add package LiteDB
   dotnet add package LiteDB.Vector
   ```
2. **Register the plugin** when constructing the database:
   ```csharp
   using var db = new LiteDatabase(
       connectionString,
       plugins: new ILitePlugin[] { VectorSearchPlugin.Instance });
   ```
3. **(For plugin authors) Register extension points inside `Initialize`**—LiteDB.Vector does this for you, but custom plugins follow the same pattern:
   ```csharp
   public void Initialize(LiteDatabase database, ILitePluginContext context)
   {
       context.RegisterBsonType(new BsonTypeRegistration(
           pluginId: "LiteDB.Vector",
           typeCode: 0x80,                 // legacy alias 0x64 kept for existing files
           name: "Vector",
           serializer: VectorBsonSerializer.WriteAsync,
           deserializer: VectorBsonSerializer.ReadAsync,
           legacyAliases: new byte[] { (byte)BsonType.Vector }));

       context.IndexMetadata.Register(new PluginIndexMetadataDescriptor(
           pluginId: "LiteDB.Vector",
           indexKind: "vector",
           serialize: VectorIndexMetadataSerializer.Serialize,
           deserialize: VectorIndexMetadataSerializer.Deserialize));

       context.RegisterPageFactory(new PageFactoryRegistration(
           pluginId: "LiteDB.Vector",
           pageType: "VectorIndex",
           numericCode: 0x85,
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
   }
   ```
   These registrations ensure collection pages, BSON serialization, and page factories all route through the plugin; without them, LiteDB will warn once on open and throw a `VectorCompatibility.PluginRequired` exception only when vector assets are accessed.
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
       Console.WriteLine($"Vector plugin required: {ex.Message}");
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
6. **Optional: inspect raw metadata from inside the plugin**—for example, `VectorIndexStrategy` can deserialize payloads using the registered descriptor:
   ```csharp
   public sealed class VectorIndexStrategy : CustomIndexStrategy
   {
       public override PluginIndexMetadata DeserializeMetadata(byte[] payload)
       {
           var slot = payload[0];
           var dimensions = BitConverter.ToUInt16(payload, 1);
           var metric = payload[3];
           return new PluginIndexMetadata("vector", new VectorMetadata(slot, dimensions, metric));
       }
   }
   ```
7. **Testing**: execute both `dotnet test LiteDB.Tests --filter Vector` and `dotnet test LiteDB.Vector.Tests` to validate optional-plugin behavior.
