using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;

namespace WriteControllerExperiments;

// Fixed-size protocol: int64 request ID + int32 value; reply echoes ID + status.
// Same-user only. One outstanding request per connection. No reconnect/retry.
internal sealed class PipeController : IDisposable
{
    private readonly WriteController _writer;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, NamedPipeServerStream> _connections = new();
    private readonly List<Task> _sessions = new();
    private readonly Task _accept;
    private int _nextId;
    internal string Name { get; }

    internal PipeController(WriteController writer, string name)
    {
        _writer = writer;
        Name = name;
        _accept = Accept();
    }

    private async Task Accept()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 128,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(_stop.Token); }
                catch { pipe.Dispose(); throw; }
                var id = Interlocked.Increment(ref _nextId);
                _connections[id] = pipe;
                lock (_sessions)
                {
                    _sessions.RemoveAll(task => task.IsCompletedSuccessfully);
                    _sessions.Add(Serve(id, pipe));
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task Serve(int connection, NamedPipeServerStream pipe)
    {
        var bytes = new byte[12];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await pipe.ReadExactlyAsync(bytes, _stop.Token);
                var id = BinaryPrimitives.ReadInt64LittleEndian(bytes);
                var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
                var status = 0;
                try { await _writer.Submit(new WriteRequest(id, value)); }
                catch (BatchRejectedException) { status = 1; }
                catch (OutcomeUnknownException) { status = 2; }
                catch (InvalidOperationException) { status = 3; }
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), status);
                await pipe.WriteAsync(bytes, _stop.Token);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            _connections.TryRemove(connection, out _);
            pipe.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _accept.GetAwaiter().GetResult();
        foreach (var pipe in _connections.Values) pipe.Dispose();
        Task[] sessions;
        lock (_sessions) sessions = _sessions.ToArray();
        Task.WhenAll(sessions).GetAwaiter().GetResult();
        _stop.Dispose();
    }
}

internal sealed class PipeWriter : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly byte[] _bytes = new byte[12];

    internal PipeWriter(string name)
    {
        _pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { _pipe.Connect(10000); }
        catch { _pipe.Dispose(); throw; }
    }

    internal void Write(WriteRequest request)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_bytes, request.Id);
        BinaryPrimitives.WriteInt32LittleEndian(_bytes.AsSpan(8), request.Value);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            _pipe.WriteAsync(_bytes, deadline.Token).AsTask().GetAwaiter().GetResult();
            _pipe.ReadExactlyAsync(_bytes, deadline.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            _pipe.Dispose();
            throw new OutcomeUnknownException(error);
        }
        if (BinaryPrimitives.ReadInt64LittleEndian(_bytes) != request.Id)
        {
            _pipe.Dispose();
            throw new OutcomeUnknownException(new IOException("Reply ID mismatch."));
        }
        switch (BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(8)))
        {
            case 0: return;
            case 1: throw new BatchRejectedException(new IOException("Server rolled back batch."));
            case 3: throw new InvalidOperationException("Server rejected unaccepted request.");
            default: throw new OutcomeUnknownException(new IOException("Server could not confirm commit."));
        }
    }

    public void Dispose() => _pipe.Dispose();
}
