using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Client.Shared;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleSharedPinCancellation_Tests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Closing_cancels_pin_acquisition_without_releasing_another_facades_owner(bool encrypted)
        {
            using var file = new TempFile();
            var connection = new ConnectionString { Filename = file.Filename, Connection = ConnectionType.Shared,
                Password = encrypted ? "secret" : null };
            using (var owner = new LiteDatabase(connection))
            {
                var rows = owner.GetCollection("rows");
                rows.Insert(Enumerable.Range(0, 150).Select(i => new BsonDocument { ["_id"] = i, ["value"] = i }));
                rows.EnsureIndex("value");
                owner.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1, ["value"] = "untouched" });
                var waiting = new LiteDatabase(connection);
                var engine = (SharedEngine)typeof(LiteDatabase)
                    .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(waiting);
                var turnstile = (SharedMutexTurnstile)typeof(SharedEngine)
                    .GetField("_turnstile", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
                using var readerReady = new ManualResetEventSlim();
                using var beginWrite = new ManualResetEventSlim();
                using var reachedNativeWait = new ManualResetEventSlim();
                var call = Task.Factory.StartNew(() => Record.Exception(() =>
                {
                    using var reader = waiting.Execute("SELECT $ FROM rows");
                    Assert.True(reader.Read());
                    readerReady.Set();
                    Assert.True(beginWrite.Wait(TimeSpan.FromSeconds(5)));
                    // The streamed snapshot belongs to this thread, so this write
                    // must acquire a pin holder instead of ordinary owner admission.
                    waiting.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 999, ["value"] = 999 });
                }), TaskCreationOptions.LongRunning);
                ILiteTransaction transaction = null;
                try
                {
                    Assert.True(readerReady.Wait(TimeSpan.FromSeconds(5)));
                    transaction = owner.BeginTransaction();
                    transaction.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 200, ["value"] = 200 });
                    turnstile.BeforeMainWait = () => reachedNativeWait.Set();
                    beginWrite.Set();
                    Assert.True(reachedNativeWait.Wait(TimeSpan.FromSeconds(5)));
                    Assert.False(call.IsCompleted);
                    waiting.Dispose();
                    Assert.True(call.Wait(TimeSpan.FromSeconds(5)));
                    Assert.IsType<OperationCanceledException>(call.Result);
                    Assert.Equal(151, transaction.GetCollection("rows").Count());
                    transaction.Commit();
                }
                finally
                {
                    transaction?.Dispose();
                    beginWrite.Set();
                    waiting.Dispose();
                    Assert.True(call.Wait(TimeSpan.FromSeconds(5)));
                }
            }
            using var cold = new LiteDatabase(connection);
            var expected = Enumerable.Range(0, 150).Concat(new[] { 200 }).ToArray();
            Assert.Equal(expected, cold.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32).OrderBy(id => id));
            Assert.Equal(expected, cold.GetCollection("rows").Find(Query.GTE("value", 0)).Select(row => row["value"].AsInt32).OrderBy(id => id));
            Assert.Equal("untouched", cold.GetCollection("sentinel").FindById(1)["value"].AsString);
        }
    }
}
