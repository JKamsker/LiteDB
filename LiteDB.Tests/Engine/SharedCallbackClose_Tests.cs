using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class SharedCallbackClose_Tests
    {
        [Theory]
        [InlineData(null, 0)]
        [InlineData(null, 1)]
        [InlineData(null, 2)]
        [InlineData("secret", 0)]
        [InlineData("secret", 1)]
        [InlineData("secret", 2)]
        public void Refused_raw_callback_close_keeps_native_ownership_until_operation_returns(string password, int path)
        {
            using var file = new TempFile();
            Action callback = null;
            using var shared = new SharedEngine(new EngineSettings
            {
                Filename = file, Password = password,
                ReadTransform = (_, value) => { var run = callback; callback = null; run?.Invoke(); return value; }
            });
            shared.EnsureIndex("rows", "value", "$.value", false);
            shared.Insert("rows", new[] { new BsonDocument { ["_id"] = 0, ["value"] = 0 } }, BsonAutoId.Int32);
            var callbacks = 0;
            var refused = false;
            var prematurelyReleased = false;
            void CloseInsideCall()
            {
                callbacks++;
                Assert.True(shared.MutexOwner.IsOwnedByCurrentThread);
                Assert.False(shared.MutexOwner.OwnsDirectly);
                refused = Record.Exception(shared.Dispose) is InvalidOperationException;
                var probe = new Thread(() =>
                {
                    if (!shared.MutexOwner.Mutex.WaitOne(0)) return;
                    prematurelyReleased = true;
                    shared.MutexOwner.Mutex.ReleaseMutex();
                });
                probe.Start();
                Assert.True(probe.Join(TimeSpan.FromSeconds(5)));
            }
            if (path != 0)
            {
#pragma warning disable CS0618
                if (path == 1) Assert.True(shared.BeginTrans());
#pragma warning restore CS0618
                callback = CloseInsideCall;
                using var reader = shared.Query("rows", new Query { Limit = 1 });
                Assert.True(reader.Read());
                Assert.False(prematurelyReleased);
                reader.Dispose();
                if (path == 1) Assert.True(shared.Commit());
            }
            else
            {
                IEnumerable<BsonDocument> Input()
                {
                    yield return new BsonDocument { ["_id"] = 1, ["value"] = 1 };
                    CloseInsideCall();
                    yield return new BsonDocument { ["_id"] = 2, ["value"] = 2 };
                }
                Assert.Equal(2, shared.Insert("rows", Input(), BsonAutoId.Int32));
            }
            Assert.Equal(1, callbacks);
            Assert.True(refused);
            Assert.False(prematurelyReleased);
            // Refusal must leave the engine usable and final disposal retryable.
            shared.Insert("rows", new[] { new BsonDocument { ["_id"] = 3, ["value"] = 3 } }, BsonAutoId.Int32);
            shared.Dispose();
            using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
            var expected = path != 0 ? new[] { 0, 3 } : new[] { 0, 1, 2, 3 };
            Assert.Equal(expected, cold.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32).OrderBy(id => id));
            var seek = cold.GetCollection("rows").Query().Where(Query.EQ("value", 3));
            Assert.StartsWith("INDEX SEEK", seek.GetPlan()["index"]["mode"].AsString);
            Assert.Equal(3, Assert.Single(seek.ToArray())["_id"].AsInt32);
        }
    }
}
