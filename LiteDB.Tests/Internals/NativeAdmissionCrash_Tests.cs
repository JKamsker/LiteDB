#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Engine;
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
        public async Task Death_during_handoff_releases_native_locks_and_preserves_recoverable_data(string stage, string password)
        {
            var directory = Path.Combine(Path.GetTempPath(), "litedb-native-crash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var filename = Path.Combine(directory, "data.db");
            var phase = "seed";
            try
            {
                NativeAdmission_Tests.Seed(filename, password);
                using (var child = new MvccProcess("native-rebuild-hold", filename, password, stage))
                {
                    phase = "wait for child boundary";
                    await child.Expect("ready");
                    phase = "kill and await child exit";
                    await child.Kill();
                }
                phase = "verify recovery marker, refusal and candidate";
                VerifyRecovery(directory, filename, stage, password);
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
                PreserveEvidence(directory, phase, error);
                throw;
            }
        }

        private static void VerifyRecovery(string directory, string filename, string stage, string password)
        {
            var marker = RebuildRecovery.GetMarkerFilename(filename);
            if (stage == "before-recovery-marker")
            {
                File.Exists(marker).Should().BeFalse();
                NativeAdmission_Tests.Verify(filename, password);
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
                            { Filename = filename, Password = password, Connection = connection });
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
            NativeAdmission_Tests.Verify(recovered, password);
            NativeAdmission_Tests.Verify(recovered, password);
            // Recovery at a separate path needs no stale-admission cleanup.
            Directory.GetFiles(directory, "*-shared-mode").Should().BeEmpty();
        }

        private void PreserveEvidence(string directory, string phase, Exception error)
        {
            // Keep the exercised directory on its original system-temp volume.
            // CI uploads copies only after the child's using scope has exited.
            var destination = Environment.GetEnvironmentVariable("LITEDB_SHARED_DIAGNOSTICS");
            if (string.IsNullOrEmpty(destination)) return;
            try
            {
                destination = Path.Combine(destination, Path.GetFileName(directory));
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "failure.txt"),
                    "Original fixture: " + directory + "\nPhase: " + phase + "\n" + error);
                foreach (var source in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(destination, Path.GetRelativePath(directory, source));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    try { File.Copy(source, target); }
                    catch (Exception copyError) { _output.WriteLine("Could not copy {0}: {1}", source, copyError); }
                }
            }
            catch (Exception copyError) { _output.WriteLine("Could not copy retained fixture: {0}", copyError); }
        }
    }
}
#endif
