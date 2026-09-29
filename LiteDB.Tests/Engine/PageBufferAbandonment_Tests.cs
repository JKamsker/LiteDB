using System;
using System.Runtime.CompilerServices;
using System.Threading;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(SharedTeardownOverlapCollection.Name)]
    public class PageBufferAbandonment_Tests
    {
        [Fact]
        public void Live_cache_and_standalone_frames_keep_strict_leak_diagnostic()
        {
            using var cache = new MemoryCache(new[] { 4 }, 8192 * 8);
            var frame = cache.NewPage();
            Assert.True(frame.FinalizedWithLiveOwner);
            Assert.Throws<InvalidOperationException>(cache.Dispose);
            cache.DiscardPage(frame);
            var standalone = new PageBuffer(new byte[8192], 0, 1);
            Assert.True(standalone.FinalizedWithLiveOwner);

            var observed = 0;
            GC.Collect(); GC.WaitForPendingFinalizers();
            PageBuffer.FinalizedInUse = counter => { if (counter == -1) Interlocked.Increment(ref observed); };
            try
            {
                var orphan = Orphan(cache);
                for (var i = 0; i < 10 && Volatile.Read(ref observed) == 0; i++)
                { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Assert.False(orphan.IsAlive);
                Assert.Equal(1, observed);
                GC.KeepAlive(cache);
            }
            finally { PageBuffer.FinalizedInUse = null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference Orphan(MemoryCache cache)
        {
            // Deliberately corrupt the ownership graph: a cache-owned frame missing from its
            // pool must remain diagnosable while that cache is live.
            var frame = new PageBuffer(new byte[8192], 0, 1)
            { Cache = cache, OwnerLiveness = new WeakReference<MemoryCache>(cache), ShareCounter = -1 };
            Assert.True(frame.FinalizedWithLiveOwner);
            return new WeakReference(frame);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Abandoned_dirty_legacy_Direct_graph_preserves_committed_state(bool promote)
        {
            using var file = new TempFile();
            var reference = Abandon(file, promote);
            for (var i = 0; i < 100 && (reference.IsAlive || NativeAdmissionDirectPool_Tests.Locked(file)); i++)
            { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(20); }
            Assert.False(reference.IsAlive);
            Assert.False(NativeAdmissionDirectPool_Tests.Locked(file));
            for (var reopen = 0; reopen < 2; reopen++)
            {
                using var db = new LiteDatabase(file);
                Assert.Equal(1, db.GetCollection("rows").Count());
                Assert.NotNull(db.GetCollection("rows").FindOne("value = 'committed'"));
                Assert.Null(db.GetCollection("rows").FindById(2));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference Abandon(string file, bool promote)
        {
            var db = new LiteDatabase(new ConnectionString { Filename = file, TransactionPageLimit = 1 });
            var rows = db.GetCollection("rows");
            rows.EnsureIndex("value");
            rows.Insert(new BsonDocument { ["_id"] = 1, ["value"] = "committed" });
            if (promote) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Assert.True(db.BeginTrans());
            rows.Insert(new BsonDocument { ["_id"] = 2, ["payload"] = new string('x', 50000) });
            Assert.Equal(2, rows.Count());
            return new WeakReference(db);
        }
    }
}
