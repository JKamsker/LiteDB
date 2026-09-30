using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;

namespace LiteDB.Fuzz.Targets;

/// <summary>One independently watched actor using the fuzz runner's child-process entry point.</summary>
internal sealed class SharedLifecycleProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _log;
    private readonly Task<string> _stderr;
    private Task<Observation> _line;
    private readonly Task _stdout;
    private readonly Channel<Observation> _events = Channel.CreateUnbounded<Observation>();
    private readonly object _logGate = new();
    private sealed record Observation(string Value, long At);
    private readonly string _actor;
    private int _completed;
    private long _invokedAt = Stopwatch.GetTimestamp();
    private long _lastCompletedAt = Stopwatch.GetTimestamp();

    internal SharedLifecycleProcess(string database, string directory, string actor, bool encrypted)
    {
        _actor = actor;
        _log = Path.Combine(directory, actor + ".jsonl");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        Add("--child", "shared-lifecycle"); Add("--database", database);
        Add("--ledger", Path.Combine(directory, actor + ".boundary"));
        Add("--worker-id", encrypted ? "1" : "0");
        _process = Process.Start(start) ?? throw new InvalidOperationException("Child did not start.");
        _stderr = _process.StandardError.ReadToEndAsync();
        _stdout = ReadOutput();
        Record("database", database);
        Record("spawn", _process.Id.ToString());
        void Add(string name, string value) { start.ArgumentList.Add(name); start.ArgumentList.Add(value); }
    }

    internal void Send(string command)
    {
        _invokedAt = Stopwatch.GetTimestamp();
        Record("invoke", command);
        _process.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
    }

    internal async Task Expect(string expected, TimeSpan? testDeadline = null)
    {
        // Each command has its own deadline. Peer progress never extends it.
        var deadline = testDeadline ?? TimeSpan.FromSeconds(20);
        while (true)
        {
            _line ??= _events.Reader.ReadAsync().AsTask();
            var remaining = deadline - Stopwatch.GetElapsedTime(_invokedAt);
            if (!_line.IsCompleted && (remaining <= TimeSpan.Zero || await Task.WhenAny(_line, Task.Delay(remaining)) != _line))
                throw Stalled();
            var observation = await _line;
            _line = null;
            if (TimeSpan.FromSeconds((observation.At - _invokedAt) / (double)Stopwatch.Frequency) > deadline)
                throw Stalled();
            var actual = observation.Value;
            if (actual == "native-wait" && expected != actual) continue;
            if (actual != expected)
                throw new FuzzFailureException("SHARED_LIFECYCLE_PROTOCOL",
                    $"Actor {_actor}: expected {expected}, received {actual ?? "EOF"}.");
            if (expected != "native-wait") { _completed++; _lastCompletedAt = observation.At; }
            return;
        }
        FuzzFailureException Stalled() => new("SHARED_LIFECYCLE_ACTOR_STALLED",
            $"Actor {_actor} failed to complete {expected}; completed commands={_completed}.");
    }

    private async Task ReadOutput()
    {
        try
        {
            string value;
            while ((value = await _process.StandardOutput.ReadLineAsync()) != null)
            {
                var observation = new Observation(value, Stopwatch.GetTimestamp());
                Record("observed", value);
                _events.Writer.TryWrite(observation);
            }
            _events.Writer.TryWrite(new Observation(null, Stopwatch.GetTimestamp()));
            _events.Writer.TryComplete();
        }
        catch (Exception error) { _events.Writer.TryComplete(error); throw; }
    }

    internal async Task AssertBlocked()
    {
        _line ??= _events.Reader.ReadAsync().AsTask();
        if (await Task.WhenAny(_line, Task.Delay(150)) == _line)
            throw new FuzzFailureException("SHARED_LIFECYCLE_OWNERSHIP_RELEASED", "Native waiter advanced while owner remained active.");
        Record("excluded", "native waiter remained blocked after observed admission");
    }

    internal bool HasExited => _process.HasExited;
    internal bool Stopped { get; private set; }

    internal async Task Kill()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Stopped = true;
        Record("killed", _process.ExitCode.ToString());
    }

    internal async Task Finish()
    {
        Send("exit");
        await Expect("closed");
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Stopped = true;
        if (_process.ExitCode != 0) throw new FuzzFailureException("SHARED_LIFECYCLE_EXIT", _actor + " failed.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) await Kill();
            Stopped = true;
            await _stdout.WaitAsync(TimeSpan.FromSeconds(10));
            File.WriteAllText(_log + ".stderr", await _stderr.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally { _process.Dispose(); }
    }

    private void Record(string kind, string value)
    {
        lock (_logGate) File.AppendAllText(_log,
            System.Text.Json.JsonSerializer.Serialize(new { actor = _actor, kind, value, completed = _completed,
                invokedTicks = _invokedAt, lastCompletedTicks = _lastCompletedAt, time = DateTime.UtcNow }) + "\n");
    }
}
