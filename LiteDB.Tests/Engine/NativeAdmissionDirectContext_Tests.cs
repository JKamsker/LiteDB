using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteDB.Engine;
using LiteDB.Tests.Issues;
using Xunit;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionDirectContext_Tests
    {
        private static BsonDocument Row(int id) => new BsonDocument { ["_id"] = id, ["value"] = id * 10 };

        [Theory]
        [InlineData("insert")]
        [InlineData("update")]
        [InlineData("upsert")]
        [InlineData("delete")]
        [InlineData("drop")]
        [InlineData("rename")]
        [InlineData("index")]
        [InlineData("begin")]
        [InlineData("pragma")]
        [InlineData("rebuild")]
        [InlineData("for-update")]
        [InlineData("into")]
        [InlineData("nested-sql")]
        public void Read_only_context_rejects_writes_without_stopping_its_writable_host(string operation)
        {
            using var file = new TempFile();
            using (var writer = new LiteDatabase(file))
            {
                writer.GetCollection("rows").Insert(Row(1));
                writer.GetCollection("rows").EnsureIndex("value", true);
                writer.Checkpoint();
                var before = TempFile.ReadAllBytesShared(file);
                using var reader = new LiteDatabase(new ConnectionString { Filename = file, ReadOnly = true });
                Assert.Same(NativeAdmissionDirectPool_Tests.Engine(writer), NativeAdmissionDirectPool_Tests.Engine(reader));
                var rows = reader.GetCollection("rows");
                Action write = () =>
                {
                    switch (operation)
                    {
                        case "insert": rows.Insert(Row(2)); break;
                        case "update": rows.Update(Row(1)); break;
                        case "upsert": rows.Upsert(Row(2)); break;
                        case "delete": rows.Delete(1); break;
                        case "drop": reader.DropCollection("rows"); break;
                        case "rename": reader.RenameCollection("rows", "renamed"); break;
                        case "index": rows.EnsureIndex("new", "value"); break;
                        case "begin": reader.BeginTrans(); break;
                        case "pragma": reader.UserVersion = 42; break;
                        case "rebuild": reader.Rebuild(); break;
                        case "for-update": using (var result = reader.Execute("SELECT $ FROM rows FOR UPDATE")) while (result.Read()) { } break;
                        case "into": using (var result = reader.Execute("SELECT $ INTO copied FROM rows")) while (result.Read()) { } break;
                        case "nested-sql": using (var result = reader.Execute("SELECT $ FROM $query('INSERT INTO rows VALUES {_id:2, value:20}')")) while (result.Read()) { } break;
                    }
                };
                var failure = Record.Exception(write);
                Assert.True(failure is IOException || failure is NotSupportedException, failure?.ToString() ?? "Write was admitted");
                reader.Checkpoint();
                Assert.Equal(before, TempFile.ReadAllBytesShared(file));
                Assert.Equal(1, rows.Count());
                Assert.False(rows.EnsureIndex("value", true));
                Assert.True(reader.Execute("SELECT readOnly FROM $database").ToArray().Single()["readOnly"].AsBoolean);
                writer.GetCollection("rows").Insert(Row(3));
                Assert.Equal(2, rows.Count());
            }
            using var cold = new LiteDatabase(file);
            Assert.Equal(new[] { 1, 3 }, cold.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32));
            Assert.Equal(3, cold.GetCollection("rows").FindOne("value = 30")["_id"].AsInt32);
        }

        [Fact]
        public void Read_only_existing_index_does_not_checkpoint_a_writable_hosts_eligible_wal()
        {
            using var file = new TempFile();
            using var writer = new LiteDatabase(file);
            writer.CheckpointSize = 0;
            writer.GetCollection("rows").Insert(Row(1));
            writer.GetCollection("rows").EnsureIndex("value", "value");
            var engine = NativeAdmissionDirectPool_Tests.Engine(writer);
            // Set the threshold directly: the public setter itself commits and checkpoints.
            var header = (HeaderPage)typeof(LiteEngine).GetField("_header",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(engine);
            header.Pragmas.Set(Pragmas.CHECKPOINT, 1, validate: false);
            var stages = 0;
            engine.CheckpointStage = stage => stages++;
            using var reader = new LiteDatabase(new ConnectionString { Filename = file, ReadOnly = true });
            var beforeData = TempFile.ReadAllBytesShared(file);
            var beforeLog = TempFile.ReadAllBytesShared(FileHelper.GetLogFile(file));
            Assert.False(reader.GetCollection("rows").EnsureIndex("value", "value"));
            Assert.Equal(0, stages);
            Assert.Equal(beforeData, TempFile.ReadAllBytesShared(file));
            Assert.Equal(beforeLog, TempFile.ReadAllBytesShared(FileHelper.GetLogFile(file)));
            Assert.Equal(1, writer.CheckpointSize);
            Assert.True(beforeLog.Length >= Constants.PAGE_SIZE);
            Assert.False(writer.GetCollection("rows").EnsureIndex("value", "value"));
            Assert.True(stages > 0, "The same operation in the writer must reach automatic checkpoint.");
            Assert.Equal(1, reader.GetCollection("rows").Count());
        }

        [Fact]
        public async Task Foreign_context_disposal_rolls_back_only_its_work_and_releases_collection_locks()
        {
            using var file = new TempFile();
            using (var first = new LiteDatabase(new ConnectionString { Filename = file, TransactionPageLimit = 1 }))
            using (var second = new LiteDatabase(file))
            {
                first.GetCollection("rows").Insert(Row(1));
                first.GetCollection("rows").EnsureIndex("value", true);
                first.BeginTrans();
                first.GetCollection("rows").Insert(new BsonDocument { ["_id"] = 2, ["value"] = 20, ["payload"] = new string('x', 50000) });
                var transaction = NativeAdmissionDirectPool_Tests.Engine(first).GetMonitor().Transactions.Single();
                transaction.Safepoint();
                Assert.True(transaction.Pages.DirtyPages.Count > 0);
                second.BeginTrans();
                second.GetCollection("other").Insert(Row(3));
                var disposed = Task.Run(() => first.Dispose());
                Assert.True(disposed.Wait(TimeSpan.FromSeconds(10)));
                // Complete on the actual opening thread; awaiting above could migrate it.
                Assert.True(second.Commit());
                second.GetCollection("rows").Insert(Row(4));
                second.Checkpoint();
                await disposed;
            }
            for (var repeat = 0; repeat < 2; repeat++)
            {
                using var cold = new LiteDatabase(file);
                Assert.Equal(new[] { 1, 4 }, cold.GetCollection("rows").FindAll().Select(row => row["_id"].AsInt32));
                Assert.Null(cold.GetCollection("rows").FindOne("value = 20"));
                Assert.NotNull(cold.GetCollection("other").FindById(3));
            }
        }

        [Fact]
        public void Per_context_page_limits_and_invalid_time_policy_coexist()
        {
            using var file = new TempFile();
            var strictSettings = new ConnectionString { Filename = file, TransactionPageLimit = 1, RejectInvalidLocalTime = true };
            var relaxedSettings = new ConnectionString { Filename = file, TransactionPageLimit = 37 };
            using (var strict = new LiteDatabase(strictSettings.CreateEngine(settings => settings.LocalTimeZone = Issue2357InvalidTime_Tests.DstZone)))
            using (var relaxed = new LiteDatabase(relaxedSettings))
            {
                strict.BeginTrans();
                strict.GetCollection("strict").Insert(Row(1));
                relaxed.BeginTrans();
                relaxed.GetCollection("relaxed").Insert(Row(2));
                var transactions = NativeAdmissionDirectPool_Tests.Engine(strict).GetMonitor().Transactions;
                Assert.Equal(new[] { 1, 37 }, transactions.Select(item => item.MaxTransactionSize).OrderBy(value => value));
                var invalid = new BsonDocument { ["_id"] = 3, ["date"] = Issue2357InvalidTime_Tests.Gap };
                Assert.Throws<ArgumentException>(() => strict.GetCollection("strict").Insert(invalid));
                Assert.False(strict.Commit());
                relaxed.GetCollection("relaxed").Insert(invalid);
                Assert.True(relaxed.Commit());
                Assert.Equal(0, strict.GetCollection("strict").Count());
                Assert.Equal(2, relaxed.GetCollection("relaxed").Count());
            }
            using var cold = new LiteDatabase(file);
            Assert.Equal(0, cold.GetCollection("strict").Count());
            Assert.Equal(new[] { 2, 3 }, cold.GetCollection("relaxed").FindAll().Select(row => row["_id"].AsInt32));
        }

        [Fact]
        public void Lazy_transforms_keep_their_context_across_interleaving_and_nested_callbacks()
        {
            using var file = new TempFile();
            using (var seed = new LiteDatabase(file)) seed.GetCollection("rows").Insert(new[] { Row(1), Row(2), Row(3) });
            var connection = new ConnectionString { Filename = file };
            using var second = new LiteDatabase(connection.CreateEngine(settings => settings.ReadTransform = (collection, value) =>
            { value.AsDocument["session"] = "second"; return value; }));
            using var first = new LiteDatabase(connection.CreateEngine(settings => settings.ReadTransform = (collection, value) =>
            {
                Assert.Equal("second", second.GetCollection("rows").FindById(1)["session"].AsString);
                value.AsDocument["session"] = "first";
                return value;
            }));
            using var firstReader = first.Execute("SELECT $ FROM rows");
            using var secondReader = second.Execute("SELECT $ FROM rows");
            first.Dispose();
            for (var i = 0; i < 3; i++)
            {
                Assert.True(firstReader.Read());
                Assert.True(secondReader.Read());
                Assert.Equal("first", firstReader.Current["session"].AsString);
                Assert.Equal("second", secondReader.Current["session"].AsString);
            }
            Assert.False(firstReader.Read());
            Assert.False(secondReader.Read());
        }
    }
}
