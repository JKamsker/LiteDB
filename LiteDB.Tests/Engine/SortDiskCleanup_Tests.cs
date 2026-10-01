using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Engine;
using Xunit;
#if !NETFRAMEWORK
using LiteDB.Internals;
#endif

namespace LiteDB.Tests.Engine
{
    public class SortDiskCleanup_Tests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Writable_open_reclaims_stale_sort_file_without_a_new_spill(bool shared, bool encrypted)
        {
            using var file = new TempFile();
            var password = encrypted ? "sort-cleanup" : null;
            Seed(file, password);
            var temp = FileHelper.GetTempFile(file);
            File.WriteAllBytes(temp, new byte[] { 9, 7, 5, 3 });
            try
            {
                using (var db = new LiteDatabase(new ConnectionString { Filename = file, Password = password,
                    Connection = shared ? ConnectionType.Shared : ConnectionType.Direct }))
                {
                    Verify(db);
                    var rows = db.GetCollection("rows");
                    rows.Update(rows.FindById(42)).Should().BeTrue();
                }
                File.Exists(temp).Should().BeFalse("a writable core owns cleanup even if it never spilled");
                using (var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password }))
                    Verify(cold);
            }
            finally { File.Delete(temp); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Failed_alias_admission_cannot_delete_live_owners_sort_file(bool encrypted)
        {
            using var file = new TempFile();
            var password = encrypted ? "sort-cleanup" : null;
            Seed(file, password);
            var temp = FileHelper.GetTempFile(file);
            var sentinel = new byte[] { 4, 8, 15, 16, 23, 42 };
#if !NETFRAMEWORK
            await MvccProcess.Run("native-raw-probe", file, password, "released");
#endif
            try
            {
                using (var owner = new LiteDatabase(new ConnectionString { Filename = file, Password = password }))
                {
                    File.WriteAllBytes(temp, sentinel);
                    var alias = Path.Combine(Path.GetDirectoryName(file), ".", Path.GetFileName(file));
                    Action rejected = () =>
                    {
                        using var peer = new LiteEngine(new EngineSettings { Filename = alias, Password = password });
                    };
                    rejected.Should().Throw<DatabaseAdmissionException>();
#if !NETFRAMEWORK
                    // Raw native probes bypass engine path normalization. Windows extended
                    // paths do not accept the lexical dot component in alias.
                    await MvccProcess.Run("native-raw-probe", file, password, "held");
                    await MvccProcess.Run("native-rejected", alias, password, "direct");
#else
                    await Task.CompletedTask;
#endif
                    File.ReadAllBytes(temp).Should().Equal(sentinel);
                    Verify(owner);
                }
                File.Exists(temp).Should().BeFalse();
#if !NETFRAMEWORK
                await MvccProcess.Run("native-raw-probe", file, password, "released");
#endif
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                Verify(cold);
            }
            finally { File.Delete(temp); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Read_only_peer_close_preserves_an_active_sort_file(bool encrypted)
        {
            using var file = new TempFile();
            var password = encrypted ? "sort-cleanup" : null;
            Seed(file, password);
            var temp = FileHelper.GetTempFile(file);
            try
            {
                var settings = new EngineSettings { Filename = file, Password = password, ReadOnly = true };
                using (var engine = new LiteEngine(settings))
                using (var owner = new LiteDatabase(engine))
                {
                    var sortField = typeof(LiteEngine).GetField("_sortDisk", BindingFlags.Instance | BindingFlags.NonPublic);
                    ((SortDisk)sortField.GetValue(engine)).Dispose();
                    var header = (HeaderPage)typeof(LiteEngine).GetField("_header", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
                    var sort = new SortDisk(settings.CreateTempFactory(), Constants.PAGE_SIZE, header.Pragmas);
                    sortField.SetValue(engine, sort);
                    using var reader = owner.GetCollection("rows").Query().OrderBy("key").ToEnumerable().GetEnumerator();
                    reader.MoveNext().Should().BeTrue();
                    sort.HasSpilled.Should().BeTrue("the owner must have a live disk-backed sort");
                    File.Exists(temp).Should().BeTrue();
                    using (var peer = new LiteDatabase(new ConnectionString { Filename = file, Password = password, ReadOnly = true }))
                        peer.GetCollection("rows").FindById(42)["value"].AsInt32.Should().Be(42);
                    File.Exists(temp).Should().BeTrue("a read-only peer cannot claim another reader's sort file");
                    var ids = new System.Collections.Generic.List<int> { reader.Current["_id"].AsInt32 };
                    while (reader.MoveNext()) ids.Add(reader.Current["_id"].AsInt32);
                    ids.Should().Equal(Enumerable.Range(0, 200).Reverse());
                }
                File.Exists(temp).Should().BeFalse("the spilling owner still deletes its own sort file");
                using var cold = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
                Verify(cold);
            }
            finally { File.Delete(temp); }
        }

        private static void Seed(string file, string password)
        {
            using var db = new LiteDatabase(new ConnectionString { Filename = file, Password = password });
            var rows = db.GetCollection("rows");
            rows.InsertBulk(Enumerable.Range(0, 200).Select(id => new BsonDocument
                { ["_id"] = id, ["value"] = id, ["key"] = (199 - id).ToString("D3") + new string('s', 100) }));
            rows.EnsureIndex("value");
            db.GetCollection("untouched").Insert(new BsonDocument { ["_id"] = 1, ["payload"] = "preserved" });
        }

        private static void Verify(LiteDatabase db)
        {
            var rows = db.GetCollection("rows");
            rows.Query().OrderBy("_id").ToArray().Select(row => row["value"].AsInt32).Should().Equal(Enumerable.Range(0, 200));
            rows.Query().Where("value = 42").GetPlan()["index"].AsDocument["mode"].AsString.Should().StartWith("INDEX SEEK");
            rows.Find("value = 42").Single()["_id"].AsInt32.Should().Be(42);
            db.GetCollection("untouched").FindById(1)["payload"].AsString.Should().Be("preserved");
        }
    }
}
