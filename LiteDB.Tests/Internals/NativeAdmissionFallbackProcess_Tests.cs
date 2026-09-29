#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Client.Shared;
using LiteDB.Internals;
using LiteDB.Tests.Engine;
using Xunit;

namespace LiteDB.Tests.Internals
{
    public class NativeAdmissionFallbackProcess_Tests
    {
        [Theory]
        [InlineData("direct", "direct", false)]
        [InlineData("direct", "shared", false)]
        [InlineData("shared", "direct", false)]
        [InlineData("direct", "direct-readonly", false)]
        [InlineData("direct-readonly", "direct", false)]
        [InlineData("direct-readonly", "shared", false)]
        [InlineData("shared", "direct-readonly", false)]
        [InlineData("shared", "shared-sha1", false)]
        [InlineData("shared-sha1", "shared", false)]
        [InlineData("shared", "shared", true)]
        [InlineData("shared-readonly", "shared", true)]
        [InlineData("direct-readonly", "direct-readonly", true)]
        public async Task Host_authority_enforces_cross_process_families_and_releases_after_death(string owner, string contender, bool compatible)
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            var before = File.ReadAllBytes(file);
            using (var holder = new MvccProcess("native-hold", file, null, owner + "|fallback"))
            {
                await holder.Expect("ready");
                await MvccProcess.Run("native-raw-probe", file, null, "fallback|held");
                await MvccProcess.Run("native-raw-probe", file, null, "released"); // DB itself has no admission lock.
                await MvccProcess.Run(compatible ? "native-open" : "native-rejected", file, null, contender + "|fallback");
                TempFile.ReadAllBytesShared(file).Should().Equal(before);
                await holder.Kill();
            }
            await MvccProcess.Run("native-raw-probe", file, null, "fallback|released");
            for (var i = 0; i < 2; i++) await MvccProcess.Run("native-open", file, null, contender + "|fallback");
            NativeAdmission_Tests.Verify(file);
        }

        [Fact]
        public async Task Fallback_streaming_reader_prevents_another_process_from_writing_or_checkpointing()
        {
            using var file = new TempFile();
            using (var seed = new LiteDatabase(file))
            {
                seed.GetCollection("rows").InsertBulk(Enumerable.Range(1, 300).Select(i =>
                    new BsonDocument { ["_id"] = i, ["value"] = i * 2 }));
                seed.GetCollection("rows").EnsureIndex("value");
            }
            using var reader = new MvccProcess("native-stream-hold", file, null, "shared|fallback");
            await reader.Expect("ready");
            using var writer = new MvccProcess("native-update", file, null, "shared|fallback");
            await writer.Expect("attempting");
            var completion = writer.ReadLine(TimeSpan.FromSeconds(20));
            await Task.Delay(200);
            completion.IsCompleted.Should().BeFalse("the active streaming snapshot retains writer ownership");
            await reader.Finish(release: true);
            (await completion).Should().Be("done");
            await writer.Finish();
            using var volume = new NativeAdmissionFallback_Tests.UnqualifiedVolume(file);
            for (var i = 0; i < 2; i++)
            {
                using var cold = new LiteDatabase(NativeAdmissionFallback_Tests.Settings(file));
                cold.GetCollection("rows").Count().Should().Be(300);
                cold.GetCollection("rows").Find("value = 999").Single()["_id"].AsInt32.Should().Be(1);
            }
        }

        [Theory]
        [InlineData("before-log-backup")]
        [InlineData("after-source-backup")]
        [InlineData("after-temp-install")]
        public async Task Fallback_replacement_never_publishes_an_unlocked_authority(string stage)
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            using var child = new MvccProcess("native-rebuild-hold", file, null, stage + "|fallback");
            await child.Expect("ready");
            await MvccProcess.Run("native-rejected", file, null, "shared|fallback");
            await MvccProcess.Run("native-rejected", file, null, "direct|fallback");
            if (stage == "before-log-backup" || stage == "after-temp-install")
                await MvccProcess.Run("native-raw-probe", file, null, "fallback|held");
            child.Send("continue");
            await child.Expect("installed");
            await MvccProcess.Run("native-raw-probe", file, null, "fallback|held");
            await MvccProcess.Run("native-rejected", file, null, "shared|fallback");
            await child.Finish(release: true);
            await MvccProcess.Run("native-open", file, null, "direct|fallback");
            NativeAdmission_Tests.Verify(file);
        }

        [Fact]
        public async Task Remote_idle_shared_owner_blocks_fallback_replacement_before_marker()
        {
            using var file = new TempFile();
            NativeAdmission_Tests.Seed(file);
            using var volume = new NativeAdmissionFallback_Tests.UnqualifiedVolume(file);
            using var db = new LiteDatabase(NativeAdmissionFallback_Tests.Settings(file, true));
            using (var child = new MvccProcess("native-hold", file, null, "shared|fallback"))
            {
                await child.Expect("ready");
                Action rebuild = () => db.Rebuild();
                rebuild.Should().Throw<IOException>().WithMessage("*Close other processes*");
                File.Exists(LiteDB.Engine.RebuildRecovery.GetMarkerFilename(file)).Should().BeFalse();
                await child.Kill();
            }
            db.Rebuild();
            await MvccProcess.Run("native-raw-probe", file, null, "fallback|held");
            db.GetCollection("untouched").FindById(1)["value"].AsInt32.Should().Be(99);
        }
    }
}
#endif
