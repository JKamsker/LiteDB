using System.Diagnostics;
using LiteDB.Fuzz.Targets;
using Xunit;

namespace LiteDB.Fuzz.Tests;

public sealed class SharedLifecycleProgress_Tests
{
    [Fact]
    public async Task One_stalled_actor_fails_even_while_an_independent_peer_completes_commands()
    {
        var directory = Path.Combine(Path.GetTempPath(), "litedb-lifecycle-watchdog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var complete = false;
        try
        {
            var path = Path.Combine(directory, "oracle.db");
            SharedLifecycleOracle.Seed(path, null);
            await using var stalled = new SharedLifecycleProcess(path, directory, "stalled", false);
            await using var peer = new SharedLifecycleProcess(path, directory, "peer", false);
            await Task.WhenAll(stalled.Expect("ready"), peer.Expect("ready"));
            stalled.Send("stop:watchdog-control"); await stalled.Expect("stop:watchdog-control");
            stalled.Send("read");
            var waiting = stalled.Expect("read", TimeSpan.FromMilliseconds(500));
            for (var i = 0; i < 5; i++) { peer.Send("read"); await peer.Expect("read"); }
            var error = await Assert.ThrowsAsync<FuzzFailureException>(() => waiting);
            Assert.Contains("failed to complete read", error.Message);
            await peer.Finish();
            complete = true;
        }
        finally { if (complete) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Waiting_later_does_not_reset_the_command_invocation_deadline()
    {
        var directory = Path.Combine(Path.GetTempPath(), "litedb-lifecycle-deadline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var complete = false;
        try
        {
            var path = Path.Combine(directory, "oracle.db");
            SharedLifecycleOracle.Seed(path, null);
            await using var actor = new SharedLifecycleProcess(path, directory, "stalled", false);
            await actor.Expect("ready");
            actor.Send("stop:watchdog-control"); await actor.Expect("stop:watchdog-control");
            actor.Send("read");
            await Task.Delay(300);
            var elapsed = Stopwatch.StartNew();
            await Assert.ThrowsAsync<FuzzFailureException>(() => actor.Expect("read", TimeSpan.FromMilliseconds(200)));
            Assert.True(elapsed.Elapsed < TimeSpan.FromMilliseconds(150), "Expect reset the expired command deadline.");
            complete = true;
        }
        finally { if (complete) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task An_unreached_native_marker_is_not_accepted_as_deadlock_prevention()
    {
        var directory = Path.Combine(Path.GetTempPath(), "litedb-lifecycle-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var complete = false;
        try
        {
            var path = Path.Combine(directory, "oracle.db");
            SharedLifecycleOracle.Seed(path, null);
            await using var actor = new SharedLifecycleProcess(path, directory, "reader", false);
            await actor.Expect("ready"); actor.Send("read");
            var error = await Assert.ThrowsAsync<FuzzFailureException>(() => actor.Expect("native-wait"));
            Assert.Contains("expected native-wait, received read", error.Message);
            await actor.Finish(); complete = true;
        }
        finally { if (complete) Directory.Delete(directory, true); }
    }
}
