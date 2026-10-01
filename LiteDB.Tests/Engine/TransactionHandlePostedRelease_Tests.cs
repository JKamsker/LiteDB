using System;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandlePostedRelease_Tests
    {
        [Fact]
        public async Task Zero_timeout_refuses_posted_legacy_release_while_bounded_retry_waits_for_it()
        {
            using var file = new TempFile();
            using var engine = new SharedEngine(new EngineSettings { Filename = file });
            using var owner = new LiteDatabase(engine, disposeOnClose: false);
            using var waiter = TransactionHandleAdmission_Tests.Open(file);
            owner.BeginTrans();
            owner.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 1 });
            using var posted = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var waiting = new ManualResetEventSlim();
            engine.MutexOwner.BeforePostedRelease = () =>
            {
                posted.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            };
            try
            {
                Assert.True(owner.Commit());
                Assert.True(posted.Wait(TimeSpan.FromSeconds(5)));
                Assert.Throws<TimeoutException>(() => waiter.BeginTransaction(TimeSpan.Zero));
                TransactionAdmission.Observe = stage => { if (stage == "native-wait") waiting.Set(); };
                var pending = Task.Run(() =>
                {
                    using var tx = waiter.BeginTransaction(TimeSpan.FromSeconds(5));
                    Assert.NotNull(tx.GetCollection("sentinel").FindById(1));
                    tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2 });
                    tx.Commit();
                });
                Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(pending.IsCompleted);
                release.Set();
                await pending;
            }
            finally
            {
                release.Set();
                engine.MutexOwner.BeforePostedRelease = null;
                engine.MutexOwner.WaitForRelease();
                TransactionAdmission.Observe = null;
            }
            waiter.Dispose(); owner.Dispose(); engine.Dispose();
            using var cold = new LiteDatabase(file);
            Assert.NotNull(cold.GetCollection("sentinel").FindById(1));
            Assert.NotNull(cold.GetCollection("rows").FindById(2));
        }
    }
}
