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
            var retentionSentinel = Environment.GetEnvironmentVariable("LITEDB_NATIVE_CRASH_RETENTION_SENTINEL") == "1";
            try
            {
                NativeAdmission_Tests.Seed(filename, password);
                using var volume = fallback ? new NativeAdmissionFallback_Tests.UnqualifiedVolume(filename) : null;
                using (var child = new MvccProcess("native-rebuild-hold", filename, password, stage + (fallback ? "|fallback" : "")))
                {
                    childPid = child.Id;
                    phase = "wait for child boundary";
                    await child.Expect("ready");
                    phase = "kill and await child exit";
                    await child.Kill();
                    childExited = true;
                }
                phase = "verify recovery marker, refusal and candidate";
                if (retentionSentinel)
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
                ReportFailureDiagnostics(filename, stage, password != null, fallback, phase, childPid, childExited, error);
                RetainedTestFixture.PublishNativeCrash(directory, $"stage={stage}; encrypted={password != null}; fallback={fallback}; phase={phase}",
                    childPid, childExited, error, _output);
                throw;
            }
        }

        private void ReportFailureDiagnostics(string filename, string stage, bool encrypted, bool fallback,
            string phase, int childPid, bool childExited, Exception primary)
        {
            // Never add I/O between child exit and the original recovery assertions.
            // A diagnostic failure must neither replace the primary nor skip retention.
            try
            {
                _output.WriteLine("Crash case stage={0}, encrypted={1}, phase={2}, child={3}, exit observed={4}\nRetained fixture: {5}\n{6}",
                    stage, encrypted, phase, childPid, childExited, Path.GetDirectoryName(filename), primary);
                var authority = File.Exists(filename) ? filename : FileHelper.GetSuffixFile(filename, "-temp", false);
                _output.WriteLine("Failure-only native admission probe: requested path={0}, fallback={1}, marker exists={2}",
                    authority, fallback, File.Exists(RebuildRecovery.GetMarkerFilename(filename)));
                using var volume = fallback ? new NativeAdmissionFallback_Tests.UnqualifiedVolume(authority) : null;
                using var probe = new DatabaseFileLock(authority, readOnly: true, create: false, fallback);
                _output.WriteLine("Failure-only native admission probe: selected authority={0}, host local={1}, admission conflicts={2}",
                    probe.AuthorityPath, probe.HostLocal, probe.Conflicts(DatabaseFileLock.Admission));
            }
            catch (Exception diagnostic)
            {
                try { _output.WriteLine("Failure-only native admission diagnostic failed: {0}", diagnostic); }
                catch { /* Preserve the original failure and publish its manifest. */ }
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
