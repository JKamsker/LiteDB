using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class TransactionHandleChildCloseRace_Tests
    {
        [Theory]
        [InlineData(false, false, null)]
        [InlineData(false, true, null)]
        [InlineData(true, false, null)]
        [InlineData(true, true, null)]
        [InlineData(false, false, "secret")]
        [InlineData(false, true, "secret")]
        [InlineData(true, false, "secret")]
        [InlineData(true, true, "secret")]
        public async Task Admitted_child_call_finishes_when_disposal_hands_cleanup_to_session(bool shared, bool enumerator, string password)
        {
            using var file = new TempFile();
            var settings = new ConnectionString { Filename = file, Password = password,
                Connection = shared ? ConnectionType.Shared : ConnectionType.Direct };
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            using (var db = new LiteDatabase(settings))
            {
                db.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 1, ["value"] = 10 });
                db.GetCollection("rows").EnsureIndex("value");
                db.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 9 });
                using var tx = db.BeginTransaction();
                tx.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 20 });
                IDisposable child;
                Func<bool> step;
                if (enumerator)
                {
                    var iterator = tx.GetCollection("rows").FindAll().GetEnumerator();
                    var innerField = iterator.GetType().GetField("_inner", BindingFlags.Instance | BindingFlags.NonPublic);
                    innerField.SetValue(iterator, new BlockingEnumerator((IEnumerator<BsonDocument>)innerField.GetValue(iterator), entered, resume));
                    child = iterator;
                    step = iterator.MoveNext;
                }
                else
                {
                    var reader = tx.GetCollection("rows").Query().ExecuteReader();
                    var innerField = reader.GetType().GetField("_inner", BindingFlags.Instance | BindingFlags.NonPublic);
                    innerField.SetValue(reader, new BlockingReader((IBsonDataReader)innerField.GetValue(reader), entered, resume));
                    child = reader;
                    step = reader.Read;
                }
                var operation = Task.Run(step);
                Task closing = null;
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    var lifetime = (SessionLifetime)typeof(LiteDatabase).GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(db);
                    closing = Task.Run(db.Dispose);
                    Assert.True(SpinWait.SpinUntil(() => lifetime.Closing.IsCancellationRequested, TimeSpan.FromSeconds(5)));
                    child.Dispose();
                    child.Dispose();
                    Assert.False(operation.IsCompleted);
                    Assert.False(closing.IsCompleted);
                }
                finally { resume.Set(); }
                Assert.True(await operation);
                await closing;
                Assert.Equal(LiteTransactionState.RolledBack, tx.State);
                Assert.Throws<ObjectDisposedException>(() => step());
            }
            for (var reopen = 0; reopen < 2; reopen++)
            {
                using var cold = new LiteDatabase(settings);
                Assert.Equal(new[] { 1 }, cold.GetCollection("rows").Find(Query.GTE("value", 10)).Select(x => x["_id"].AsInt32));
                Assert.Equal(1, cold.GetCollection("rows").Count());
                Assert.NotNull(cold.GetCollection("sentinel").FindById(9));
            }
        }

        private sealed class BlockingReader : IBsonDataReader
        {
            private readonly IBsonDataReader _inner;
            private readonly ManualResetEventSlim _entered, _resume;
            internal BlockingReader(IBsonDataReader inner, ManualResetEventSlim entered, ManualResetEventSlim resume) { _inner = inner; _entered = entered; _resume = resume; }
            public BsonValue this[string field] => _inner[field];
            public BsonValue Current => _inner.Current;
            public string Collection => _inner.Collection;
            public bool HasValues => _inner.HasValues;
            public bool Read() { _entered.Set(); if (!_resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return _inner.Read(); }
            public void Dispose() => _inner.Dispose();
        }
        private sealed class BlockingEnumerator : IEnumerator<BsonDocument>
        {
            private readonly IEnumerator<BsonDocument> _inner;
            private readonly ManualResetEventSlim _entered, _resume;
            internal BlockingEnumerator(IEnumerator<BsonDocument> inner, ManualResetEventSlim entered, ManualResetEventSlim resume) { _inner = inner; _entered = entered; _resume = resume; }
            public BsonDocument Current => _inner.Current;
            object IEnumerator.Current => Current;
            public bool MoveNext() { _entered.Set(); if (!_resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return _inner.MoveNext(); }
            public void Reset() => _inner.Reset();
            public void Dispose() => _inner.Dispose();
        }
    }
}
