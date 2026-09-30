#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Engine;
using LiteDB.Client.Shared;
using LiteDB.Internals;
using LiteDB.Tests.Engine;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Tests.Internals
{
    public class NativeAdmissionCrash_Tests
    {
        private readonly ITestOutputHelper _output;
        public NativeAdmissionCrash_Tests(ITestOutputHelper output) { _output = output; }

        [Theory]
        [InlineData("before-recovery-marker", null)]
        [InlineData("before-recovery-marker-flush", null)]
        [InlineData("after-log-backup", null)]
        [InlineData("after-source-backup", null)]
        [InlineData("after-temp-install", null)]
        [InlineData("before-recovery-marker-delete", null)]
        [InlineData("before-recovery-marker", "secret")]
        [InlineData("before-recovery-marker-flush", "secret")]
        [InlineData("after-log-backup", "secret")]
        [InlineData("after-source-backup", "secret")]
        [InlineData("after-temp-install", "secret")]
        [InlineData("before-recovery-marker-delete", "secret")]
        [InlineData("before-recovery-marker", null, true)]
        [InlineData("before-recovery-marker-flush", null, true)]
        [InlineData("after-log-backup", null, true)]
        [InlineData("after-source-backup", null, true)]
        [InlineData("after-temp-install", null, true)]
        [InlineData("before-recovery-marker-delete", null, true)]
        [InlineData("before-recovery-marker", "secret", true)]
        [InlineData("before-recovery-marker-flush", "secret", true)]
        [InlineData("after-log-backup", "secret", true)]
        [InlineData("after-source-backup", "secret", true)]
        [InlineData("after-temp-install", "secret", true)]
        [InlineData("before-recovery-marker-delete", "secret", true)]
        public async Task Death_during_handoff_releases_native_locks_and_preserves_recoverable_data(string stage, string password, bool fallback = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "litedb-native-crash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var filename = Path.Combine(directory, "data.db");
            var phase = "seed";
            var childPid = 0;
            var childExited = false;
            try
            {
                NativeAdmission_Tests.Seed(filename, password);
                using var volume = fallback ? new NativeAdmissionFallback_Tests.UnqualifiedVolume(filename) : null;
                using (var child = new MvccProcess("native-rebuild-hold", filename, password, stage + (fallback ? "|fallback" : "")))
                {
                    childPid = child.Id;
                    phase = "wait for child boundary";
                    await child.Expect("ready");
                    if (stage == "before-recovery-marker-flush")
                    {
                        phase = "prove live child owns the exclusive recovery marker";
                        Action readLiveMarker = () => File.ReadAllBytes(RebuildRecovery.GetMarkerFilename(filename));
                        readLiveMarker.Should().Throw<IOException>();
                    }
                    phase = "kill and await child exit";
                    await child.Kill();
                    childExited = true;
                    _output.WriteLine("Child {0} exit observed, exit code {1}", childPid, child.ExitCode);
                }
                phase = "probe native admission after child exit";
                var authority = File.Exists(filename) ? filename : FileHelper.GetSuffixFile(filename, "-temp", false);
                using (var probe = new DatabaseFileLock(authority, readOnly: true, create: false, fallback))
                    probe.Conflicts(DatabaseFileLock.Admission).Should().BeFalse("the exited child must release native admission");
                _output.WriteLine("Native admission released for {0}; marker exists={1}", authority,
                    File.Exists(RebuildRecovery.GetMarkerFilename(filename)));
                phase = "verify recovery marker, refusal and candidate";
                if (Environment.GetEnvironmentVariable("LITEDB_NATIVE_CRASH_RETENTION_SENTINEL") == "1")
                    throw new InvalidOperationException("native crash retention sentinel after child exit");
                VerifyRecovery(directory, filename, stage, password, fallback);
                phase = "cleanup after all recovery assertions passed";
                // Process termination and successful recovery assertions do not
                // prevent a separate Windows handle from briefly denying deletion.
                // Retry only sharing/lock violations; persistent failures still fail.
                FileHelper.Exec(5, () => Directory.Delete(directory, true));
            }
            catch (Exception error)
            {
                _output.WriteLine("Crash case stage={0}, encrypted={1}, phase={2}\nRetained fixture: {3}\n{4}",
                    stage, password != null, phase, directory, error);
                RetainedTestFixture.PublishNativeCrash(directory, $"stage={stage}; encrypted={password != null}; fallback={fallback}; phase={phase}",
                    childPid, childExited, error, _output);
                throw;
            }
        }

        private static void VerifyRecovery(string directory, string filename, string stage, string password, bool fallback)
        {
            var marker = RebuildRecovery.GetMarkerFilename(filename);
            if (stage == "before-recovery-marker")
            {
                File.Exists(marker).Should().BeFalse();
                NativeAdmission_Tests.Verify(filename, password, fallback);
                return;
            }
            var files = Directory.GetFiles(directory);
            var bytes = Array.ConvertAll(files, File.ReadAllBytes);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                foreach (var connection in new[] { ConnectionType.Direct, ConnectionType.Shared })
                {
                    Action open = () =>
                    {
                        using var db = new LiteDatabase(new ConnectionString
                            { Filename = filename, Password = password, Connection = connection, AllowHostLocalAdmissionFallback = fallback });
                        db.GetCollection("rows").Count();
                    };
                    open.Should().Throw<LiteException>().Which.ErrorCode.Should().Be(LiteException.REBUILD_INCOMPLETE);
                }
            }
            Directory.GetFiles(directory).Should().BeEquivalentTo(files);
            for (var i = 0; i < files.Length; i++) File.ReadAllBytes(files[i]).Should().Equal(bytes[i]);
            var candidate = stage == "after-temp-install" || stage == "before-recovery-marker-delete"
                ? filename : FileHelper.GetSuffixFile(filename, "-temp", false);
            var recovered = Path.Combine(directory, "recovered.db");
            File.Copy(candidate, recovered);
            using var recoveredVolume = fallback ? new NativeAdmissionFallback_Tests.UnqualifiedVolume(recovered) : null;
            NativeAdmission_Tests.Verify(recovered, password, fallback);
            NativeAdmission_Tests.Verify(recovered, password, fallback);
            // Recovery at a separate path needs no stale-admission cleanup.
            Directory.GetFiles(directory, "*-shared-mode").Should().BeEmpty();
        }

    }
}
#endif
