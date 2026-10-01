using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using LiteDB.Engine;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    [Collection(NativeFileSyncCollection.Name)]
    public class TransactionHandleSettingsLifetime_Tests
    {
        private sealed class ApplicationState
        {
            internal LiteDatabase Database;
            internal ILiteTransaction Transaction;
        }

        private sealed class ApplicationSettings : EngineSettings
        {
            internal readonly ApplicationState State = new ApplicationState();
        }

        private sealed class ApplicationCollation : Collation
        {
            internal readonly ApplicationState State = new ApplicationState();
            internal ApplicationCollation() : base("tr-TR/None") { }
            public override string ToString() => "en-US/IgnoreCase";
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shared_holder_does_not_root_application_fields_in_settings_or_collation(bool customSettings)
        {
            using var file = new TempFile();
            WeakReference[] references = null;
            TransactionHandle_Tests.OnThread(() => references = Abandon(file, customSettings));
            for (var attempt = 0; attempt < 100 &&
                (references.Any(reference => reference.IsAlive) || NativeAdmissionDirectPool_Tests.Locked(file)); attempt++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(20);
            }
            Assert.Equal(new[] { false, false, false }, references.Select(reference => reference.IsAlive));
            Assert.False(NativeAdmissionDirectPool_Tests.Locked(file));
            var connection = new ConnectionString { Filename = file, Password = "settings-secret" };
            for (var reopen = 0; reopen < 2; reopen++)
            {
                using var cold = new LiteDatabase(connection);
                Assert.Equal("en-US/IgnoreCase", cold.Collation.ToString());
                Assert.NotNull(cold.GetCollection("rows").FindOne("word = 'KEPT'"));
                Assert.Equal(1, cold.GetCollection("rows").Count());
                Assert.Null(cold.GetCollection("rows").FindById(2));
            }
            connection.Connection = ConnectionType.Shared;
            using var peer = new LiteDatabase(connection);
            using var retry = peer.BeginTransaction(TimeSpan.FromSeconds(5));
            Assert.NotNull(retry.GetCollection("rows").FindById(1));
            retry.Commit();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] Abandon(string file, bool customSettings)
        {
            var settings = new ApplicationSettings
            {
                Filename = file, Password = "settings-secret", TransactionPageLimit = 1,
                Collation = new Collation("en-US/IgnoreCase")
            };
            var collation = new ApplicationCollation();
            var db = customSettings ? new LiteDatabase(new SharedEngine(settings)) :
                new LiteDatabase(new ConnectionString
                {
                    Filename = file, Password = "settings-secret", TransactionPageLimit = 1,
                    Connection = ConnectionType.Shared, Collation = collation
                });
            var state = customSettings ? settings.State : collation.State;
            state.Database = db;
            // Initialize the file through the handle so snapshot policy is exercised too.
            using (var committed = db.BeginTransaction())
            {
                var rows = committed.GetCollection("rows");
                rows.EnsureIndex("word");
                rows.Insert(new BsonDocument { ["_id"] = 1, ["word"] = "kept" });
                Assert.NotNull(rows.FindOne("word = 'KEPT'"));
                committed.Commit();
            }
            var abandoned = db.BeginTransaction();
            state.Transaction = abandoned;
            abandoned.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["payload"] = new string('x', 50000) });
            Assert.Equal(2, abandoned.GetCollection("rows").Count());
            return new[] { new WeakReference(db), new WeakReference(abandoned), new WeakReference(state) };
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Derived_settings_snapshot_preserves_explicit_and_profile_transaction_limits(bool explicitLimit)
        {
            var settings = new ApplicationSettings { MemoryProfile = MemoryProfile.LowMemory };
            if (explicitLimit) settings.TransactionPageLimit = 19;
            var snapshot = settings.SnapshotForTransactionHolder();
            Assert.IsType<EngineSettings>(snapshot);
            Assert.Null(snapshot.Collation);
            Assert.Equal(settings.TransactionPageLimit, snapshot.TransactionPageLimit);
            settings.MemoryProfile = snapshot.MemoryProfile = MemoryProfile.Balanced;
            Assert.Equal(settings.TransactionPageLimit, snapshot.TransactionPageLimit);
        }
    }
}
