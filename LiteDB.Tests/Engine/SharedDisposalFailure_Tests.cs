using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class SharedDisposalFailure_Tests
    {
        private static object Field(object target, string name) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        [Theory]
        [InlineData(null, 0)]
        [InlineData(null, 1)]
        [InlineData(null, 2)]
        [InlineData("secret", 0)]
        [InlineData("secret", 1)]
        [InlineData("secret", 2)]
        public void Pin_close_failure_finishes_connection_cleanup_and_preserves_leased_reader(string password, int failures)
        {
            using var file = new TempFile();
            var settings = new EngineSettings { Filename = file, Password = password };
            var shared = new SharedEngine(settings);
            settings = (EngineSettings)Field(shared, "_settings");
            var db = new LiteDatabase(shared);
            var rows = db.GetCollection("rows");
            rows.EnsureIndex("value");
            rows.Insert(Enumerable.Range(0, 150).Select(i => new BsonDocument { ["_id"] = i, ["value"] = i }));
            db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 7, ["value"] = "untouched" });
            using var reader = db.Execute("SELECT $ FROM rows");
            Assert.True(reader.Read());
            rows.Insert(new BsonDocument { ["_id"] = 200, ["value"] = 200 });
#pragma warning disable CS0618
            Assert.True(db.BeginTrans());
#pragma warning restore CS0618
            rows.Insert(new BsonDocument { ["_id"] = 201, ["value"] = 201 });
            Assert.NotNull(Field(shared, "_pin"));
            var registry = Field(shared, "_readers");
            var slots = Field(registry, "_slots");
            Assert.NotNull(slots);
            var primary = new IOException("pin core cleanup failed");
            var secondary = new InvalidOperationException("parent cleanup failed");
            using var reached = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var core = (LiteEngine)Field(shared, "_engine");
            var guard = typeof(LiteEngine).GetField("_modeGuard", BindingFlags.Instance | BindingFlags.NonPublic);
            guard.SetValue(core, new AfterDispose((IDisposable)guard.GetValue(core), () =>
            {
                reached.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                if (failures == 2) shared.SimulateOpenEngine = () => throw secondary;
                if (failures > 0) throw primary;
            }));
            var close = Task.Run(() => Record.Exception(db.Dispose));
            try
            {
                Assert.True(reached.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(close.IsCompleted);
                release.Set();
                Assert.True(close.Wait(TimeSpan.FromSeconds(10)));
                if (failures == 0) Assert.Null(close.Result);
                else Assert.Same(primary, close.Result);
                if (failures == 2) Assert.Contains(secondary, primary.Data.Values.Cast<object>());
                Assert.True((bool)Field(registry, "_disposed"));
                Assert.False((bool)Field(slots, "_closed"));
                var handles = Field(shared, "_handles");
                if (handles != null) Assert.True((bool)Field(handles, "_disposed"));
#if NET8_0_OR_GREATER
                Assert.Null(Field(shared, "_coordination"));
#endif
                db.Dispose();
                var count = 1;
                while (reader.Read()) count++;
                Assert.Equal(150, count);
                reader.Dispose();
                Assert.True((bool)Field(slots, "_closed"));
                shared.SimulateOpenEngine = null;
                using (var peer = new LiteDatabase(new ConnectionString { Filename = file, Password = password, Connection = ConnectionType.Shared }))
                    peer.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 300, ["value"] = 300 });
                for (var repeat = 0; repeat < 2; repeat++)
                {
                    using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                    var expected = Enumerable.Range(0, 150).Concat(new[] { 200, 300 }).ToArray();
                    var indexed = cold.GetCollection("rows").Query().Where("value >= 0");
                    Assert.StartsWith("INDEX SEEK", indexed.GetPlan()["index"]["mode"].AsString);
                    Assert.Equal(expected, indexed.ToEnumerable().Select(x => x["_id"].AsInt32).OrderBy(x => x));
                    Assert.Equal("untouched", cold.GetCollection("sentinel").FindById(7)["value"].AsString);
                }
                GC.KeepAlive(db);
            }
            finally
            {
                release.Set();
                close.Wait(TimeSpan.FromSeconds(10));
                shared.SimulateOpenEngine = null;
                reader.Dispose();
                db.Dispose();
            }
        }

        private sealed class AfterDispose : IDisposable
        {
            private readonly IDisposable _inner;
            private readonly Action _after;
            internal AfterDispose(IDisposable inner, Action after) { _inner = inner; _after = after; }
            public void Dispose() { _inner?.Dispose(); _after(); }
        }
    }
}
