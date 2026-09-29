using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB.Client.Shared;
using LiteDB.Engine;
using Xunit;
using Xunit.Abstractions;

namespace LiteDB.Tests.Engine
{
    public class NativeAdmissionIsolation_Tests
    {
        private readonly ITestOutputHelper _output;
        public NativeAdmissionIsolation_Tests(ITestOutputHelper output) { _output = output; }

        [Fact]
        public void Identity_wait_delays_unrelated_admission_and_release_but_both_resume()
        {
            using var first = new TempFile();
            using var second = new TempFile();
            NativeAdmission_Tests.Seed(first);
            NativeAdmission_Tests.Seed(second);
            using var owner = SharedModeGuard.Open(second, false, SharedMutexNameStrategy.Default);
            using var identity = new DatabaseFileLock(first, readOnly: true, create: false);
            using var waiting = new ManualResetEventSlim();
            using var releaseStarted = new ManualResetEventSlim();
            using var openStarted = new ManualResetEventSlim();
            // This thread owns A's identity mutex; the opener takes the global
            // registry gate before waiting for it. Do not await across mutex ownership.
            var gate = DatabaseAdmissionRegistry.Enter(identity.Identity);
            var blockedOpen = Task.Factory.StartNew(() =>
            {
                SharedCoordinationFile.CreationStage = (path, stage) =>
                {
                    if (stage == "mode-before-identity-lock") waiting.Set();
                };
                try { using var lease = SharedModeGuard.Open(first, false, SharedMutexNameStrategy.Default); }
                finally { SharedCoordinationFile.CreationStage = null; }
            }, TaskCreationOptions.LongRunning);
            Task release = null, open = null;
            var elapsed = Stopwatch.StartNew();
            try
            {
                waiting.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                release = Task.Factory.StartNew(() => { releaseStarted.Set(); owner.Dispose(); }, TaskCreationOptions.LongRunning);
                open = Task.Factory.StartNew(() =>
                {
                    openStarted.Set();
                    using var lease = SharedModeGuard.Open(second, false, SharedMutexNameStrategy.Default);
                }, TaskCreationOptions.LongRunning);
                releaseStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                openStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                // Characterize the existing global gate, not a throughput budget.
                release.Wait(250).Should().BeFalse();
                open.IsCompleted.Should().BeFalse();
                using var probe = new DatabaseFileLock(second, readOnly: true, create: false);
                probe.Conflicts(DatabaseFileLock.Admission).Should().BeTrue();
                _output.WriteLine("A identity mutex held; B admission/release still blocked after {0:F1} ms", elapsed.Elapsed.TotalMilliseconds);
            }
            finally
            {
                gate.Dispose();
                Task.WaitAll(new[] { blockedOpen, release ?? Task.CompletedTask, open ?? Task.CompletedTask },
                    TimeSpan.FromSeconds(10)).Should().BeTrue();
            }
            using (var probe = new DatabaseFileLock(second, readOnly: true, create: false))
                probe.Conflicts(DatabaseFileLock.Admission).Should().BeFalse();
            _output.WriteLine("All three operations completed after {0:F1} ms", elapsed.Elapsed.TotalMilliseconds);
            NativeAdmission_Tests.Verify(first);
            NativeAdmission_Tests.Verify(second);
        }
    }
}
