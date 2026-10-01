using System;
using System.Reflection;
using System.Threading;
using LiteDB.Engine;
using LiteDB.Tests.Issues;
using Xunit;
using static LiteDB.Tests.Engine.TransactionHandleChildTestAccess;

namespace LiteDB.Tests.Engine
{
    internal static class TransactionHandleChildTestAccess
    {
        internal static SharedEngine Cached(SharedEngine shared) => (SharedEngine)typeof(SharedEngine)
            .GetField("_cachedTransactionChild", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(shared);

        internal static LiteEngine Core(ILiteTransaction tx) => ((TransactionResources)typeof(LiteTransaction)
            .GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tx)).Engine;
    }

    // Deliberately compiled for CLR 4 too: the netstandard implementation has no
    // modern mapped coordinator, but must obey the same wrapper/core ownership rules.
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleChildRuntime_Tests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("secret")]
        public void Reused_holder_and_wrapper_refresh_after_independent_thread_writer_on_every_runtime(string password)
        {
            using var file = new TempFile();
            Thread worker = null;
            TransactionAdmission.Observe = stage => { if (stage == "storage-opened") worker = Thread.CurrentThread; };
            SharedHolderScheduler.IdleWaitOverride = 60000;
            try
            {
                using (var shared = new SharedEngine(new EngineSettings { Filename = file, Password = password }))
                using (var db = new LiteDatabase(shared, disposeOnClose: false))
                {
                    LiteEngine previous;
                    using (var first = db.BeginTransaction())
                    {
                        previous = Core(first);
                        first.GetCollection("rows").EnsureIndex("value");
                        first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 0 });
                        first.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
                        first.Commit();
                    }
                    var cached = Cached(shared);
                    var holder = worker;
                    Assert.NotNull(cached);
                    Assert.True(SpinWait.SpinUntil(() => SharedHolderScheduler.IsIdle(holder), TimeSpan.FromSeconds(5)));
                    for (var value = 1; value <= 3; value++)
                    {
                        var expected = value;
                        // Different Thread and SharedEngine force native writer ownership
                        // to transfer; same-thread mutex recursion cannot hide retention.
                        TransactionHandle_Tests.OnThread(() =>
                        {
                            using var peer = new LiteDatabase(new ConnectionString
                            { Filename = file, Password = password, Connection = ConnectionType.Shared });
                            Assert.True(peer.BeginTrans());
                            peer.GetCollection("rows").Update(new BsonDocument { ["_id"] = 1, ["value"] = expected });
                            Assert.True(peer.Commit());
                        });
                        using var next = db.BeginTransaction(TimeSpan.FromSeconds(5));
                        Assert.Same(holder, worker);
                        Assert.NotSame(previous, Core(next));
                        previous = Core(next);
                        Assert.Equal(value, next.GetCollection("rows").FindById(1)["value"].AsInt32);
                        Assert.Single(next.GetCollection("rows").Find(Query.EQ("value", value)));
                        Assert.NotNull(next.GetCollection("sentinel").FindById(9));
                        next.Commit();
                        Assert.Same(cached, Cached(shared));
                        Assert.True(SpinWait.SpinUntil(() => SharedHolderScheduler.IsIdle(holder), TimeSpan.FromSeconds(5)));
                    }
                }
            }
            finally
            {
                TransactionAdmission.Observe = null;
                SharedHolderScheduler.IdleWaitOverride = null;
            }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                Assert.Equal(3, cold.GetCollection("rows").FindById(1)["value"].AsInt32);
                Assert.Single(cold.GetCollection("rows").Find(Query.EQ("value", 3)));
                Assert.NotNull(cold.GetCollection("sentinel").FindById(9));
            }
        }
    }
}
