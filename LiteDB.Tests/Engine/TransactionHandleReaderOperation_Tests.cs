using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleReaderOperation_Tests
    {
        [Fact]
        public async Task Source_advancement_retains_operation_through_second_row_transform()
        {
            using var file = new TempFile();
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            using (var engine = new LiteEngine(new EngineSettings
            {
                Filename = file,
                ReadTransform = (collection, value) =>
                {
                    if (collection == "rows" && value.IsDocument && value["_id"] == 2)
                    {
                        entered.Set();
                        Assert.True(resume.Wait(TimeSpan.FromSeconds(10)));
                    }
                    return value;
                }
            }))
            {
                using var db = new LiteDatabase(engine, disposeOnClose: false);
                db.GetCollection("rows").Insert(new[]
                {
                    new BsonDocument { ["_id"] = 1 }, new BsonDocument { ["_id"] = 2 }
                });
                using var reader = engine.Query("rows", new Query { Select = BsonExpression.Create("$") });
                Assert.True(reader.Read()); // already-materialized first row
                Assert.Equal(1, reader.Current["_id"].AsInt32);
                var operations = (OperationLifetime)typeof(LiteEngine).GetField("_operations",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(engine);
                var advancing = Task.Run(reader.Read);
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    // Ignore cursor dependencies deliberately: only the executing Read
                    // operation, including its transform, can prevent this acquisition.
                    Assert.Throws<LiteException>(() =>
                    { using var exclusive = operations.Exclusive(() => true, TimeSpan.FromMilliseconds(100)); });
                }
                finally { resume.Set(); }
                Assert.True(await advancing);
                Assert.Equal(2, reader.Current["_id"].AsInt32);
                using (operations.Exclusive(() => true, TimeSpan.FromSeconds(5))) { }
            }
            using var cold = new LiteDatabase(file);
            Assert.Equal(2, cold.GetCollection("rows").Count());
            Assert.NotNull(cold.GetCollection("rows").FindById(1));
            Assert.NotNull(cold.GetCollection("rows").FindById(2));
        }
    }
}
