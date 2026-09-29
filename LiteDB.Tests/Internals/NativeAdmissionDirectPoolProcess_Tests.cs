#if NET8_0_OR_GREATER
using System;
using System.Linq;
using System.Threading.Tasks;
using LiteDB.Client.Shared;
using LiteDB.Engine;
using LiteDB.Internals;
using LiteDB.Tests.Engine;
using Xunit;

namespace LiteDB.Tests.Internals
{
    public class NativeAdmissionDirectPoolProcess_Tests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Final_database_and_reader_release_admits_other_processes(bool fallback)
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            using var volume = fallback ? new NativeAdmissionFallback_Tests.UnqualifiedVolume(file) : null;
            var family = fallback ? "direct-fallback" : "direct";
            {
                var connection = new ConnectionString { Filename = file, AllowHostLocalAdmissionFallback = fallback };
                using var first = new LiteDatabase(connection);
                using var second = new LiteDatabase(connection);
                Assert.Same(NativeAdmissionDirectPool_Tests.Engine(first), NativeAdmissionDirectPool_Tests.Engine(second));
                using var reader = first.Execute("SELECT $ FROM rows");
                Assert.True(reader.Read());
                first.Dispose();
                await MvccProcess.Run("native-rejected", file, null, family);
                second.GetCollection("sentinel").Insert(new BsonDocument { ["_id"] = 8 });
                second.Dispose();
                await MvccProcess.Run("native-raw-probe", file, null, family + "-held");
                await MvccProcess.Run("native-rejected", file, null, family);
                Assert.Equal(42, reader.Current["value"].AsInt32);
                await Task.Run(() => reader.Dispose());
                await MvccProcess.Run("native-raw-probe", file, null, family);
                await MvccProcess.Run("native-open", file, null, family);
                using var cold = new LiteDatabase(connection);
                Assert.NotNull(cold.GetCollection("sentinel").FindById(8));
                Assert.Equal(42, cold.GetCollection("rows").FindOne("value = 42")["value"].AsInt32);
            }
        }

        [Fact]
        public async Task Killed_process_releases_pooled_engine_and_preserves_both_owners_commits()
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            using (var owner = new MvccProcess("native-pool-hold", file, null, "direct"))
            {
                await owner.Expect("ready");
                await MvccProcess.Run("native-rejected", file, null, "direct");
                await MvccProcess.Run("native-raw-probe", file, null, "direct-held");
                await owner.Kill();
            }
            for (var i = 0; i < 2; i++)
            {
                await MvccProcess.Run("native-open", file, null, "direct");
                using var cold = new LiteDatabase(file);
                Assert.Equal(new[] { 42, 84 }, cold.GetCollection("rows").FindAll().OrderBy(x => x["_id"].AsInt32).Select(x => x["value"].AsInt32));
                Assert.Equal(99, cold.GetCollection("untouched").FindById(1)["value"].AsInt32);
            }
        }
    }
}
#endif
