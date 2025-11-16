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
3. **Create vector indexes via extensions** (no core APIs expose vector methods):
   ```csharp
   var options = new VectorIndexOptions(dimensions: 384);
   db.GetCollection<MyDoc>("docs").EnsureIndex(x => x.Embedding, options);
   ```
4. **Handle missing plugin scenarios**: if you omit `LiteDB.Vector`, the database stays accessible but any collection/index that references vector metadata throws a `LiteException` explaining that `LiteDB.Vector` is required.
5. **Testing**: execute both `dotnet test LiteDB.Tests --filter Vector` and `dotnet test LiteDB.Vector.Tests` to validate optional-plugin behavior.
