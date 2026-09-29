using System;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using LiteDB.Client.Direct;
using LiteDB.Engine;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionDirectPoolSettings_Tests
    {
        private sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private static LiteEngine EngineOf(LiteDatabase database) =>
            ((DirectEngineLease)database.Context.Engine).Engine;

        private static ConnectionString Settings(string filename, string password = null) =>
            new ConnectionString { Filename = filename, Password = password };

        private static BsonDocument Document(int id) => new BsonDocument { ["_id"] = id, ["value"] = id * 10 };

        [Theory]
        [InlineData("readonly")]
        [InlineData("durability")]
        [InlineData("fallback")]
        [InlineData("compact")]
        [InlineData("legacy")]
        [InlineData("upgrade")]
        [InlineData("rebuild")]
        [InlineData("local-time")]
        [InlineData("cache")]
        [InlineData("transaction-pages")]
        [InlineData("migration-limit")]
        [InlineData("collation")]
        public void Incompatible_settings_preserve_the_live_owner_and_database_bytes(string setting)
        {
            using var file = new TempFile();
            using (var owner = new LiteDatabase(file.Filename))
            {
                owner.CheckpointSize = 0;
                owner.GetCollection("rows").Insert(Document(1));
                var data = TempFile.ReadAllBytesShared(file);
                var log = FileHelper.GetLogFile(file);
                var wal = TempFile.ReadAllBytesShared(log);
                var requested = Settings(file);
                switch (setting)
                {
                    case "readonly": requested.ReadOnly = true; break;
                    case "durability": requested.DurableCommits = false; break;
                    case "fallback": requested.AllowHostLocalAdmissionFallback = true; break;
                    case "compact": requested.CompactStorage = CompactStorageMode.Legacy; break;
                    case "legacy": requested.LegacyIndexScan = true; break;
                    case "upgrade": requested.Upgrade = true; break;
                    case "rebuild": requested.AutoRebuild = true; break;
                    case "local-time": requested.RejectInvalidLocalTime = true; break;
                    case "cache": requested.CacheSize = 1024 * 1024; break;
                    case "transaction-pages": requested.TransactionPageLimit = 1; break;
                    case "migration-limit": requested.IndexMigrationLimitSize = 1024 * 1024; break;
                    case "collation": requested.Collation = new Collation("en-US/Ordinal"); break;
                }
                Action open = () => { using var rejected = new LiteDatabase(requested); };
                open.Should().Throw<DatabaseAdmissionException>();
                TempFile.ReadAllBytesShared(file).Should().Equal(data);
                TempFile.ReadAllBytesShared(log).Should().Equal(wal);
                using var sibling = new LiteDatabase(file.Filename);
                EngineOf(sibling).Should().BeSameAs(EngineOf(owner));
                sibling.GetCollection("rows").FindById(1)["value"].AsInt32.Should().Be(10);
                owner.GetCollection("rows").Insert(Document(2));
            }
            using var reopened = new LiteDatabase(file.Filename);
            reopened.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32).Should().Equal(1, 2);
            reopened.CheckpointSize = 1000;
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", null)]
        [InlineData("secret", "wrong")]
        public void Password_mismatch_cannot_attach_and_null_is_distinct_from_empty(string password, string rejectedPassword)
        {
            using var file = new TempFile();
            using (var owner = new LiteDatabase(Settings(file, password)))
            {
                owner.GetCollection("rows").Insert(Document(1));
                owner.Checkpoint();
                var bytes = TempFile.ReadAllBytesShared(file);
                Action open = () => { using var rejected = new LiteDatabase(Settings(file, rejectedPassword)); };
                open.Should().Throw<LiteException>().Which.ErrorCode.Should().Be(LiteException.INVALID_PASSWORD);
                TempFile.ReadAllBytesShared(file).Should().Equal(bytes);
                using var sibling = new LiteDatabase(Settings(file, password));
                EngineOf(sibling).Should().BeSameAs(EngineOf(owner));
                sibling.GetCollection("rows").Insert(Document(2));
            }
            using var reopened = new LiteDatabase(Settings(file, password));
            reopened.GetCollection("rows").Count().Should().Be(2);
        }

        [Fact]
        public void Read_only_owners_share_engine_but_cannot_admit_a_writable_owner()
        {
            using var file = new TempFile();
            using (var seed = new LiteDatabase(file.Filename)) seed.GetCollection("rows").Insert(Document(1));
            var bytes = File.ReadAllBytes(file);
            using (var first = new LiteDatabase(new ConnectionString { Filename = file, ReadOnly = true }))
            using (var second = new LiteDatabase(new ConnectionString { Filename = file, ReadOnly = true }))
            {
                EngineOf(second).Should().BeSameAs(EngineOf(first));
                Action open = () => { using var rejected = new LiteDatabase(file.Filename); };
                open.Should().Throw<DatabaseAdmissionException>();
                Action write = () => second.GetCollection("rows").Insert(Document(2));
                write.Should().Throw<IOException>();
                first.GetCollection("rows").Count().Should().Be(1);
            }
            File.ReadAllBytes(file).Should().Equal(bytes);
        }

        [Theory]
        [InlineData("new-password")]
        [InlineData("")]
        [InlineData(null)]
        public void Rebuild_authenticates_new_owners_against_the_live_password(string password)
        {
            using var file = new TempFile();
            using (var owner = new LiteDatabase(Settings(file, "old-password")))
            using (var sibling = new LiteDatabase(Settings(file, "old-password")))
            {
                owner.GetCollection("rows").Insert(Document(1));
                owner.GetCollection("rows").EnsureIndex("value", true);
                owner.Rebuild(new RebuildOptions { Password = password, RemovePassword = password == null });
                EngineOf(sibling).Should().BeSameAs(EngineOf(owner));
                sibling.GetCollection("rows").FindOne("value = 10")["_id"].AsInt32.Should().Be(1);
                sibling.GetCollection("rows").Insert(Document(2));
                Action oldPassword = () => { using var rejected = new LiteDatabase(Settings(file, "old-password")); };
                oldPassword.Should().Throw<LiteException>().Which.ErrorCode.Should().Be(LiteException.INVALID_PASSWORD);
                using var attached = new LiteDatabase(Settings(file, password));
                EngineOf(attached).Should().BeSameAs(EngineOf(owner));
                attached.GetCollection("rows").Insert(Document(3));
            }
            using var reopened = new LiteDatabase(Settings(file, password));
            reopened.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32).Should().Equal(1, 2, 3);
            reopened.GetCollection("rows").FindOne("value = 20")["_id"].AsInt32.Should().Be(2);
        }

        [Fact]
        public void Same_thread_join_and_sibling_disposal_leave_transaction_completion_with_the_caller()
        {
            using var file = new TempFile();
            using (var owner = new LiteDatabase(file.Filename))
            {
                owner.GetCollection("rows").Insert(Document(1));
                owner.BeginTrans().Should().BeTrue();
                owner.GetCollection("rows").Insert(Document(2));
                using (var sibling = new LiteDatabase(file.Filename))
                {
                    EngineOf(sibling).Should().BeSameAs(EngineOf(owner));
                    sibling.BeginTrans().Should().BeFalse();
                    sibling.GetCollection("rows").Insert(Document(3));
                }
                owner.GetCollection("rows").Count().Should().Be(3);
                owner.Rollback().Should().BeTrue();
                owner.GetCollection("rows").Count().Should().Be(1);
                owner.BeginTrans().Should().BeTrue();
                using (var sibling = new LiteDatabase(file.Filename)) sibling.GetCollection("rows").Insert(Document(4));
                owner.Commit().Should().BeTrue();
            }
            using var reopened = new LiteDatabase(file.Filename);
            reopened.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32).Should().Equal(1, 4);
        }

        [Fact]
        public void Foreign_thread_completion_preserves_the_owners_explicit_transaction()
        {
            using var file = new TempFile();
            using (var owner = new LiteDatabase(file.Filename))
            using (var sibling = new LiteDatabase(file.Filename))
            {
                owner.GetCollection("rows").Insert(Document(1));
                owner.BeginTrans().Should().BeTrue();
                owner.GetCollection("rows").Insert(Document(2));
                Exception failure = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        Action commit = () => sibling.Commit();
                        commit.Should().Throw<LiteException>().WithMessage("*same thread*");
                        sibling.Rollback().Should().BeFalse();
                    }
                    catch (Exception error) { failure = error; }
                }) { IsBackground = true };
                worker.Start();
                worker.Join(TimeSpan.FromSeconds(10)).Should().BeTrue();
                failure.Should().BeNull();
                owner.GetCollection("rows").Count().Should().Be(2);
                owner.Rollback().Should().BeTrue();
            }
            using var reopened = new LiteDatabase(file.Filename);
            reopened.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32).Should().Equal(1);
        }

        [Fact]
        public void Mapper_state_is_per_database_while_engine_pragmas_are_shared()
        {
            using var file = new TempFile();
            var firstMapper = new BsonMapper();
            var secondMapper = new BsonMapper();
            firstMapper.Entity<Row>().Field(row => row.Name, "first_name");
            secondMapper.Entity<Row>().Field(row => row.Name, "second_name");
            using (var first = new LiteDatabase(file.Filename, firstMapper))
            using (var second = new LiteDatabase(file.Filename, secondMapper))
            {
                EngineOf(second).Should().BeSameAs(EngineOf(first));
                first.Context.Should().NotBeSameAs(second.Context);
                first.Mapper.Should().BeSameAs(firstMapper);
                second.Mapper.Should().BeSameAs(secondMapper);
                first.GetCollection<Row>("rows").Insert(new Row { Id = 1, Name = "first" });
                second.GetCollection<Row>("rows").Insert(new Row { Id = 2, Name = "second" });
                second.GetCollection("rows").FindById(1)["first_name"].AsString.Should().Be("first");
                first.GetCollection("rows").FindById(2)["second_name"].AsString.Should().Be("second");
                first.UserVersion = 37;
                second.UserVersion.Should().Be(37);
                second.Timeout = TimeSpan.FromSeconds(17);
                first.Timeout.Should().Be(TimeSpan.FromSeconds(17));
            }
            using var reopened = new LiteDatabase(file.Filename);
            reopened.GetCollection("rows").Count().Should().Be(2);
            reopened.UserVersion.Should().Be(37);
        }
    }
}
