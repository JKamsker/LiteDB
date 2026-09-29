using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleSharedCleanup_Tests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Holder_stream_callback_rejects_indirect_session_close(bool parentClose)
        {
            using var file = new TempFile();
            using var db = new LiteDatabase(new ConnectionString { Filename = file, Connection = ConnectionType.Shared });
            var callbacks = 0;
            NativeAdmissionStreamProbe.Attach = (path, writable) => stage =>
            {
                if (stage != "disposed" || Thread.CurrentThread.Name != "LiteDB transaction mutex") return;
                Assert.Throws<InvalidOperationException>(db.Dispose);
                Interlocked.Increment(ref callbacks);
            };
            try
            {
                using var tx = db.BeginTransaction();
                tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1 });
                if (parentClose) db.Dispose();
                else tx.Rollback();
                Assert.Equal(LiteTransactionState.RolledBack, tx.State);
                Assert.True(callbacks > 0);
                if (!parentClose) Assert.Equal(0, db.GetCollection("rows").Count());
            }
            finally { NativeAdmissionStreamProbe.Attach = null; }
            db.Dispose();
            using var cold = new LiteDatabase(file);
            Assert.Equal(0, cold.GetCollection("rows").Count());
        }

        [Theory]
        [InlineData("DataStream")]
        [InlineData("LogStream")]
        [InlineData("TempStream")]
        public void Shared_caller_stream_handle_rejects_before_stream_access(string property)
        {
            using var file = new TempFile();
            using var stream = new MemoryStream();
            var settings = new EngineSettings { Filename = file };
            typeof(EngineSettings).GetProperty(property).SetValue(settings, stream);
            using var engine = new SharedEngine(settings);
            using var db = new LiteDatabase(engine, disposeOnClose: false);
            Assert.Throws<NotSupportedException>(() => db.BeginTransaction());
            Assert.Equal(0, stream.Length);
            Assert.True(stream.CanWrite);
        }

        [Fact]
        public void Fatal_cleanup_preserves_primary_and_adds_secondary_failure()
        {
            using var engine = new LiteEngine(new EngineSettings { Filename = ":memory:" });
            var original = new IOException("primary storage error");
            var cleanup = new IOException("secondary cleanup error");
            typeof(LiteEngine).GetField("_modeGuard", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(engine, new ThrowOnDispose(cleanup));
            var state = (EngineState)typeof(LiteEngine).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
            using (engine.EnterOperation())
            {
                state.Stop(original);
                Assert.Empty(original.Data);
            }
            Assert.Contains(cleanup, original.Data.Values.Cast<Exception>());
            engine.Dispose();
        }

        private sealed class ThrowOnDispose : IDisposable
        {
            private readonly Exception _error;
            internal ThrowOnDispose(Exception error) { _error = error; }
            public void Dispose() => throw _error;
        }
    }
}
