# LiteDB.Vector – Embedded Vector Search Enhanced Version of LiteDB

**LiteDB.Vector** is an open-source enhancement to the [LiteDB](https://github.com/mbdavid/LiteDB) embedded NoSQL database that adds native support for vector similarity search. It enables efficient *in-process* top-k and range-based similarity queries using cosine distance over `float[]`-based `BsonVector` values.

> ✅ MIT Licensed and fully independent of the LiteDB core project. This is a community-driven extension built with deep respect for the original work by [@mbdavid](https://github.com/mbdavid).

---

## ✨ Key Features

- 🧮 **BsonVector**: Native vector embedding support (`float[]`)
- 📏 **Cosine Similarity**: Efficient similarity filtering and ranking via `VECTOR_SIM()`
- 🔍 **Top-K & Distance-Based Queries**:
  - `.WhereNear(field, target, maxDistance)`
  - `.TopKNear(field, target, k)`
- 🧪 **Fluent and SQL Support**: Both LINQ-like and SQL-style queries supported
- ⚡ **In-Process Performance**: Sub-millisecond filtering on thousands of records
- 🔒 Fully compatible with secured + encrypted LiteDB files
- ✅ **Works anywhere LiteDB works** – no server, no extra dependencies

---

## 🧠 Use Cases

- Local semantic search for small/medium knowledgebases
- In-process Retrieval-Augmented Generation (RAG)
- Cognitive memory modeling in AI agents
- Lightweight alternatives to vector databases like Pinecone or Qdrant

---

## 💡 Examples

### Quick Example

```csharp
using var db = new LiteDatabase("mem.db");
var col = db.GetCollection("vectors");

// Insert some vectorized documents
col.Insert(new BsonDocument {
    ["_id"] = 1,
    ["Embedding"] = new BsonVector(new float[] { 1.0f, 0.0f })
});

// Query for nearby embeddings
var queryEmbedding = new float[] { 1.0f, 0.0f };
var results = col.Query()
    .WhereNear(doc => doc["Embedding"], queryEmbedding, maxDistance: 0.3)
    .ToList();
```


### Vector Search with POCO Class

You can use vector similarity search with your own classes, enabling strongly typed access to documents with embedded float arrays.

```csharp
using LiteDB;

// Your POCO model
public class MyDocument
{
    [BsonId]
    public int Id { get; set; }

    public float[] Embedding { get; set; }
}

// Open a LiteDB instance (in-memory or file-backed)
using var db = new LiteDatabase("MyData.db");

// Get typed collection
var col = db.GetCollection<MyDocument>("mydocs");

// Insert some example vectorized documents
col.Insert(new MyDocument { Id = 1, Embedding = new float[] { 1.0f, 0.0f } });
col.Insert(new MyDocument { Id = 2, Embedding = new float[] { 0.0f, 1.0f } });
col.Insert(new MyDocument { Id = 3, Embedding = new float[] { 1.0f, 1.0f } });

// Query for documents near the target vector
var target = new float[] { 1.0f, 0.0f };

var results = col
    .Query()
    .WhereNear(x => x.Embedding, target, maxDistance: 0.5)
    .ToList();

// or TopK
var topResults = col
    .Query()
    .TopKNear(x => x.Embedding, target, 2)
    .ToList();
```

### Or in SQL:

```sql
SELECT *
FROM vectors
WHERE VECTOR_SIM($.Embedding, [1.0, 0.0]) < 0.3
```

---

## 📊 Performance

Benchmarks show microsecond-level response times for Top-K and filtered nearest vector queries:

| Dataset Size | Method           | Mean Time (μs) |
|--------------|------------------|----------------|
| 100          | `WhereNear`      | ~270 μs        |
| 1000         | `TopKNearLimit`  | ~7 ms          |
| 10,000       | `TopKNearLimit`  | ~84 ms         |

> See full benchmark results [here](docs/performance)

---

## 🧩 Integration Notes

- No background indexing or ANN required
- Works with `float[]`, `BsonVector`, or JSON `[1.0, 2.0, 3.0]`
- Embeddings can be stored in any document field
- Compatible with LINQ and SQL

---

## 🧬 License & Attribution

This extension is based on [LiteDB](https://github.com/mbdavid/LiteDB), licensed under the [MIT License](https://opensource.org/licenses/MIT). We extend it under the same license and acknowledge the foundational work of [Maurício David (@mbdavid)](https://github.com/mbdavid).

---

## 🛠️ Future Roadmap (Optional)

The following features are **experimental** or under development in downstream use:

- 🔗 Semantic edge-based graph memory
- 🧠 Emotion + context-aware memory prioritization
- 🪢 Multi-store scoped memory architecture (Working, LTM, Local)
- 🌀 Integration into [SynthetikDelusion](https://github.com/hurley451/synthetikdelusion), an open cognitive agent platform

---

## 🧪 Getting Started

```bash
dotnet add package LiteDB.Vector
```

Or clone this repo and reference the project locally.

---

## 🔍 See Also

- [LiteDB – Original Project](https://github.com/mbdavid/LiteDB)
- [LiteDB Studio](https://github.com/mbdavid/LiteDB.Studio)
- [SynthetikDelusion Cognitive Agent Framework](https://github.com/hurley451/synthetikdelusion)
