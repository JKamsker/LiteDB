# Vector Search Plugin - Quickstart Guide

**Package**: LiteDB.Vector  
**Version**: 1.0  
**Prerequisites**: LiteDB v6.x, .NET Standard 2.0+ or .NET 8.0+

## Table of Contents
1. [Installation](#installation)
2. [Basic Setup](#basic-setup)
3. [Creating Vector Indexes](#creating-vector-indexes)
4. [Querying with Vector Search](#querying-with-vector-search)
5. [Distance Metrics](#distance-metrics)
6. [Common Patterns](#common-patterns)
7. [Migration from Core](#migration-from-core)
8. [Troubleshooting](#troubleshooting)

---

## Installation

### Step 1: Install NuGet Package

```bash
# Using .NET CLI
dotnet add package LiteDB.Vector

# Using Package Manager Console
Install-Package LiteDB.Vector
```

### Step 2: Add Using Statement

```csharp
using LiteDB;
using LiteDB.Vector;
```

---

## Basic Setup

### Register the Plugin

Pass the plugin when constructing the database instance:

```csharp
using var db = new LiteDatabase(
    "mydata.db",
    plugins: new[] { VectorSearchPlugin.Instance });

// Vector operations are now available
```

### Optional: Set Default Metric via Connection String

```csharp
var connectionString = "mydata.db;vector.metric=cosine";
using var db = new LiteDatabase(
    connectionString,
    plugins: new[] { VectorSearchPlugin.Instance });

// All vector indexes will use Cosine by default
```

---

## Creating Vector Indexes

### Define Your Model

```csharp
public class Article
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public float[] Embedding { get; set; }  // Vector field
}
```

### Create Index Using Lambda Expression

```csharp
var collection = db.GetCollection<Article>("articles");

// Create vector index on Embedding field
var options = new VectorIndexOptions(
    dimensions: 384,  // Match your embedding model
    metric: VectorDistanceMetric.Cosine  // Default, can be omitted
);

collection.EnsureIndex(x => x.Embedding, options);
```

### Create Index with Custom Name

```csharp
// Useful for multiple indexes on different vector fields
collection.EnsureIndex("embedding_idx", x => x.Embedding, options);
```

### Create Index Using String Expression

```csharp
// For nested fields or dynamic scenarios
var options = new VectorIndexOptions(768, VectorDistanceMetric.Euclidean);
collection.EnsureIndex("$.Metadata.Vector", options);
```

---

## Querying with Vector Search

### Top-K Nearest Neighbors (Recommended)

```csharp
// Get your query vector (from ML model, user input, etc.)
float[] queryEmbedding = GetEmbeddingFromText("search query");

// Find 10 nearest articles and include distance scores
var matches = collection
    .Query()
    .TopKNear(
        field: x => x.Embedding,
        target: queryEmbedding,
        k: 10,
        metric: VectorDistanceMetric.Cosine,
        maxDistance: 0.75)
    .WithVectorScore()
    .ToList();

// Results are automatically sorted by distance (closest first)
foreach (var match in matches)
{
    Console.WriteLine($"{match.Document.Title} - Distance: {match.Distance:F3}");
}
```

### Filter by Distance Threshold

```csharp
// Find all articles within distance threshold
var similar = collection
    .Query()
    .WhereNear(
        x => x.Embedding,
        queryEmbedding,
        maxDistance: 0.5,
        metric: VectorDistanceMetric.Euclidean)
    .ToList();
```

### Combine with Other Filters

```csharp
// Vector search + traditional filters
var results = collection
    .Query()
    .OrderByNearest(
        x => x.Embedding,
        queryEmbedding,
        metric: VectorDistanceMetric.Cosine,
        maxDistance: 0.8)
    .Where(x => x.PublishedDate > DateTime.Now.AddMonths(-6))
    .Limit(20)
    .WithVectorScore()
    .ToList();
```

### Project Distances or Similarities

```csharp
var best = collection
    .Query()
    .Nearest(
        field: x => x.Embedding,
        target: queryEmbedding,
        k: 5,
        metric: VectorDistanceMetric.DotProduct)
    .WithVectorScore(VectorScoreKind.Similarity)
    .Select(match => new
    {
        match.Document.Id,
        match.Distance,
        match.Similarity   // null for metrics without similarity transforms (e.g., Euclidean)
    })
    .ToList();
```

`WithVectorScore` reuses the planner's calculated distance. Similarity projection is available only for metrics with a defined similarity transform (cosine or dot product); attempting to use it for other metrics throws a descriptive `LiteException`. `OrderByNearest` and `Nearest` always apply deterministic (`distance`, `_id`) tie-breaking so identical scores remain stable between executions.

### Use VECTOR_DIST in Expressions

```csharp
// Custom distance filtering in WHERE clause
var query = collection.Find(
    Query.Where("VECTOR_DIST($.Embedding, @target) < 0.3", queryEmbedding)
);

// Include distance in projections
var results = collection.Find(
    Query.All(),
    "{ title: $.Title, distance: VECTOR_DIST($.Embedding, @target) }",
    queryEmbedding
);

// Similarity alias (cosine metrics only)
var similar = collection.Find(
    Query.All(),
    "{ title: $.Title, similarity: VECTOR_SIM($.Embedding, @target) }",
    queryEmbedding
);
```

---

## Convenience Helpers

```csharp
// Build vectors without touching BsonVector directly
var embedding = Vector.Create(0.1f, 0.9f, 0.3f);
var normalized = Vector.Normalize(embedding.Values);  // returns float[]

var matches = collection
    .Query()
    .TopKNear(x => x.Embedding, normalized, k: 3)
    .WithVectorScore()
    .ToList();
```

`Vector.Create`, `Vector.FromReadOnlySpan`, and `Vector.Normalize` remove the need to interact with `BsonVector` directly and ensure consistent float32 coercion.

---

## Distance Metrics

### Cosine Similarity (Default)

Best for normalized embeddings from ML models.

```csharp
var options = new VectorIndexOptions(384, VectorDistanceMetric.Cosine);
collection.EnsureIndex(x => x.Embedding, options);
```

**When to use**:
- BERT, Sentence-BERT embeddings
- OpenAI embeddings
- Most transformer-based models
- Direction matters more than magnitude

**Distance range**: [0, 2] (0 = identical, 1 = orthogonal, 2 = opposite); similarity alias returns [-1, 1]

---

### Euclidean Distance

Best for absolute distance measurements.

```csharp
var options = new VectorIndexOptions(512, VectorDistanceMetric.Euclidean);
collection.EnsureIndex(x => x.Features, options);
```

**When to use**:
- Image features (SIFT, SURF)
- Spatial coordinates
- Magnitude matters
- Physical measurements

**Distance range**: [0, +infinity) where 0 = identical

---

### Dot Product

Best for unnormalized embeddings and recommendation scores.

```csharp
var options = new VectorIndexOptions(768, VectorDistanceMetric.DotProduct);
collection.EnsureIndex(x => x.Scores, options);
```

**When to use**:
- Recommendation systems
- Unnormalized vectors
- Both direction and magnitude matter

**Distance range**: (-infinity, +infinity); interpret larger values as more similar when vectors are non-negative or normalized

---

## Common Patterns

### Pattern 1: Semantic Search

```csharp
public class Document
{
    public int Id { get; set; }
    public string Text { get; set; }
    public float[] TextEmbedding { get; set; }
}

// Setup
var collection = db.GetCollection<Document>("docs");
var options = new VectorIndexOptions(384);
collection.EnsureIndex(x => x.TextEmbedding, options);

// Insert documents with embeddings
var doc = new Document
{
    Text = "LiteDB is a serverless database",
    TextEmbedding = GetEmbedding("LiteDB is a serverless database")
};
collection.Insert(doc);

// Search
var query = "embedded database for .NET";
var queryVector = GetEmbedding(query);
var matches = collection
    .Query()
    .TopKNear(x => x.TextEmbedding, queryVector, k: 5)
    .WithVectorScore()
    .ToList();
```

---

### Pattern 2: Image Similarity

```csharp
public class Image
{
    public int Id { get; set; }
    public string Path { get; set; }
    public float[] Features { get; set; }  // CNN features
}

// Use Euclidean for image features
var options = new VectorIndexOptions(2048, VectorDistanceMetric.Euclidean);
collection.EnsureIndex(x => x.Features, options);

// Find similar images
var targetFeatures = ExtractImageFeatures("query_image.jpg");
var similar = collection
    .Query()
    .TopKNear(x => x.Features, targetFeatures, k: 20)
    .WithVectorScore()
    .ToList();
```

---

### Pattern 3: Multi-Modal Search

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public float[] TextEmbedding { get; set; }
    public float[] ImageEmbedding { get; set; }
}

// Create indexes for both modalities
var textOptions = new VectorIndexOptions(384);
var imageOptions = new VectorIndexOptions(512);

collection.EnsureIndex("text_idx", x => x.TextEmbedding, textOptions);
collection.EnsureIndex("image_idx", x => x.ImageEmbedding, imageOptions);

// Search by text
var textResults = collection
    .Query()
    .TopKNear(x => x.TextEmbedding, GetTextEmbedding("red shoes"), k: 10)
    .WithVectorScore()
    .ToList();
var imageResults = collection
    .Query()
    .TopKNear(x => x.ImageEmbedding, GetImageEmbedding("shoe.jpg"), k: 10)
    .WithVectorScore()
    .ToList();


---

### Pattern 4: Hybrid Search (Vector + Keyword)

```csharp
public class Article
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string[] Tags { get; set; }
    public float[] Embedding { get; set; }
}

// Create both traditional and vector indexes
collection.EnsureIndex(x => x.Tags);
var vectorOptions = new VectorIndexOptions(384);
collection.EnsureIndex(x => x.Embedding, vectorOptions);

// Hybrid search: semantic + keyword filter
var query = "machine learning database";
var queryVector = GetEmbedding(query);

var results = collection
    .Query()
    .WhereNear(x => x.Embedding, queryVector, maxDistance: 0.7)
    .Where(x => x.Tags.Contains("database"))  // Keyword filter
    .ToList();
```

---

### Pattern 5: Batch Processing

```csharp
// Efficient bulk insert with vector indexes
var articles = new List<Article>();

foreach (var text in textCorpus)
{
    articles.Add(new Article
    {
        Content = text,
        Embedding = await GetEmbeddingAsync(text)  // Batch embeddings if possible
    });
    
    // Insert in batches for better performance
    if (articles.Count >= 100)
    {
        collection.InsertBulk(articles);
        articles.Clear();
    }
}

if (articles.Count > 0)
{
    collection.InsertBulk(articles);
}
```

---

## Migration from Core

If you were using vector features from LiteDB core (pre-migration), follow these steps:

### Step 1: Add NuGet Package

```bash
dotnet add package LiteDB.Vector
```

### Step 2: Enable the Plugin

Pass the plugin through the `LiteDatabase` constructor:

```csharp
using var db = new LiteDatabase(
    "mydata.db",
    plugins: new[] { VectorSearchPlugin.Instance });

// Your existing code works unchanged
var collection = db.GetCollection<Article>("articles");
collection.EnsureIndex(x => x.Embedding, new VectorIndexOptions(384));

```

> **Tip:** Skipping plugin registration raises the runtime error "Vector operations require the LiteDB.Vector plugin...". Add `VectorSearchPlugin.Instance` to the constructor to resolve it.

### Step 3: Validate Existing Calls

All existing APIs remain identical:
- `collection.EnsureIndex(x => x.Embedding, options)`
- `collection.Query().TopKNear(...).WithVectorScore()`
- `VECTOR_DIST` (and optional `VECTOR_SIM`) expressions
- Existing database files remain compatible

### Step 4: Update Using Statements (Optional)

The namespace didn't change, so this is optional:

```csharp
using LiteDB;
using LiteDB.Vector;  // Optional - types are in this namespace
```

---

## Troubleshooting

### Error: "Vector operations require the LiteDB.Vector plugin"

**Cause**: Plugin not initialized or missing LiteDB.Vector reference.

**Solution**: Reference LiteDB.Vector and create the database with the plugin.
```csharp
using var db = new LiteDatabase(
    "mydata.db",
    plugins: new[] { VectorSearchPlugin.Instance });
```

---

### Error: "Query vector dimensions (256) do not match index dimensions (384)"

**Cause**: Query vector size doesn't match index configuration

**Solution**: Ensure embedding model produces vectors of correct size
```csharp
// Index created with 384 dimensions
var options = new VectorIndexOptions(384);
collection.EnsureIndex(x => x.Embedding, options);

// Query must also be 384 dimensions
float[] query = GetEmbedding("query");  // Must return 384-dimensional vector
var results = collection.Query().TopKNear(x => x.Embedding, query, k: 10).WithVectorScore().ToList();
```

---

### Slow Index Creation

**Cause**: Building HNSW graph for large collections takes time

**Solution**:
1. Create index before bulk insert
2. Insert in batches if possible
3. Expected time: O(n log n) for n documents

```csharp
// Better: Create index first
var options = new VectorIndexOptions(384);
collection.EnsureIndex(x => x.Embedding, options);

// Then insert data
collection.InsertBulk(documents);

// Worse: Insert data first, then create index (will be slow)
```

---

### Poor Search Results

**Possible causes**:
1. **Wrong metric**: Try different distance metrics
   ```csharp
   // Try Euclidean instead of Cosine
   var options = new VectorIndexOptions(384, VectorDistanceMetric.Euclidean);
   ```

2. **Unnormalized vectors**: Cosine works best with normalized embeddings
   ```csharp
   // Normalize before storing
   var normalized = Vector.Normalize(embedding);
   doc.Embedding = normalized;
   ```

3. **Dimension mismatch**: Verify embedding model output size
   ```csharp
   Console.WriteLine($"Vector size: {embedding.Length}");
   // Should match index dimensions
   ```

---

### Results Ordering Looks Different

**Cause**: Multiple documents share the same computed distance and earlier builds relied on planner iteration order.

**Solution**: Ordering now applies deterministic (`distance`, `_id`) tie-breaking. If you need a different secondary sort, append `.OrderBy(x => x.SomeField)` after calling `OrderByNearest` or project scores with `.WithVectorScore()` and `OrderBy`.

### Index Not Being Used

**Symptoms**: Queries are slow even with vector index

**Check**:
1. Verify index exists:
   ```csharp
   var indexes = collection.GetIndexes();
   foreach (var idx in indexes)
   {
       Console.WriteLine($"{idx.Name}: {idx.Expression}");
   }
   ```

2. Ensure field name matches:
   ```csharp
   // Index on "Embedding"
   collection.EnsureIndex(x => x.Embedding, options);
   
   // Query must also use "Embedding"
   .TopKNear(x => x.Vector, query, k: 10)     // ❌ Won't use index
   ```

---

## Performance Tips

### 1. Choose Right Metric

- **Cosine**: Best for most ML embeddings (default choice)
- **Euclidean**: When magnitude matters
- **DotProduct**: For recommendation scores

### 2. Dimension Considerations

- Smaller dimensions = faster indexing and search
- Typical sizes: 384 (fast), 768 (balanced), 1536 (accurate)
- Consider dimensionality reduction (PCA) for very large vectors

### 3. Query Optimization

```csharp
// Good: Specific k value
.TopKNear(x => x.Embedding, query, k: 10)

// Less efficient: Large k with LINQ filter
.TopKNear(x => x.Embedding, query, k: 1000)
.Where(x => x.Score > threshold)
.Take(10)

// Better: Use maxDistance if you know threshold
.WhereNear(x => x.Embedding, query, maxDistance: 0.5)
.Limit(10)
```

### 4. Index Creation

```csharp
// Best: Index before data
collection.EnsureIndex(x => x.Embedding, options);
collection.InsertBulk(documents);

// Good: Batch inserts
foreach (var batch in documents.Chunk(1000))
{
    collection.InsertBulk(batch);
}
```

---

## Next Steps

- Read the [API Reference](./contracts/extension-methods.md) for detailed method documentation
- See [Data Model](./data-model.md) for architecture details
- Check [Plugin API](./contracts/plugin-api.md) for advanced scenarios

---

## Additional Resources

### Example: Building a Simple RAG System

```csharp
using var db = new LiteDatabase("rag.db", plugins: new[] { VectorSearchPlugin.Instance });
var rag = new RAGExample(db);

public class RAGExample
{
    private readonly ILiteCollection<Chunk> _collection;
    
    public RAGExample(LiteDatabase db)
    {
        _collection = db.GetCollection<Chunk>("chunks");
        var options = new VectorIndexOptions(384);
        _collection.EnsureIndex(x => x.Embedding, options);
    }
    
    public void AddDocument(string text)
    {
        var chunks = SplitIntoChunks(text);
        foreach (var chunk in chunks)
        {
            _collection.Insert(new Chunk
            {
                Text = chunk,
                Embedding = GetEmbedding(chunk)
            });
        }
    }
    
    public string Query(string question)
    {
        var queryVector = GetEmbedding(question);
        var relevant = _collection
            .Query()
            .TopKNear(x => x.Embedding, queryVector, k: 3)
            .ToList();
        
        var context = string.Join("\n\n", relevant.Select(c => c.Text));
        return GenerateAnswer(question, context);
    }
}

public class Chunk
{
    public int Id { get; set; }
    public string Text { get; set; }
    public float[] Embedding { get; set; }
}
```

---

- GitHub Issues: https://github.com/mbdavid/LiteDB/issues
- Documentation: https://www.litedb.org/
- Discord: [LiteDB Community]
