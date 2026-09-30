using System.Collections.Concurrent;
using System.Diagnostics;
using LiteDB;

namespace WriteControllerExperiments;

/// <summary>
/// Experimental bounded writer queue. Each group is ONE atomic transaction.
/// Every request completes only after that transaction's durable commit returns.
/// </summary>
internal sealed class WriteController : IDisposable
{
    private sealed record Pending(WriteRequest Request, TaskCompletionSource Completion);
    private readonly BlockingCollection<Pending> _queue;
    private readonly Thread _worker;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _batchSize, _lingerMs;
    private Exception? _failure;
    internal long Commits, Completed;
    internal int LargestBatch;

    internal WriteController(Func<LiteDatabase> open, int batchSize, int lingerMs = 0, int capacity = 4096)
    {
        if (batchSize < 1 || batchSize > 256 || lingerMs < 0 || lingerMs > 1000 || capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        _batchSize = batchSize;
        _lingerMs = lingerMs;
        _queue = new BlockingCollection<Pending>(capacity);
        _worker = new Thread(() => Run(open)) { IsBackground = true, Name = "Experimental batch writer" };
        _worker.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    internal Task Submit(WriteRequest request)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!_queue.TryAdd(new Pending(request, completion)))
                completion.TrySetException(new InvalidOperationException("Queue full; request was not accepted."));
        }
        catch (InvalidOperationException)
        {
            completion.TrySetException(new InvalidOperationException("Controller closed; request was not accepted.", _failure));
        }
        return completion.Task;
    }

    private void Run(Func<LiteDatabase> open)
    {
        var batch = new List<Pending>(_batchSize);
        LiteDatabase? db = null;
        try
        {
            db = open();
            _ready.TrySetResult();
            while (_queue.TryTake(out var first, Timeout.Infinite))
            {
                batch.Add(first);
                var deadline = Stopwatch.GetTimestamp() + _lingerMs * Stopwatch.Frequency / 1000;
                while (batch.Count < _batchSize)
                {
                    var remaining = Math.Max(0, (int)Math.Ceiling((deadline - Stopwatch.GetTimestamp()) * 1000d / Stopwatch.Frequency));
                    if (!_queue.TryTake(out var next, remaining)) break;
                    batch.Add(next);
                }
                Commit(db, batch);
                batch.Clear();
            }
        }
        catch (Exception error)
        {
            _failure = error;
            _ready.TrySetException(error);
            foreach (var pending in batch) pending.Completion.TrySetException(error);
        }
        finally
        {
            _queue.CompleteAdding();
            while (_queue.TryTake(out var pending))
                pending.Completion.TrySetException(new InvalidOperationException("Writer stopped before executing request.", _failure));
            try { db?.Dispose(); }
            catch (Exception cleanup)
            {
                if (_failure == null) _failure = cleanup;
                else _failure.Data["ControllerCleanupFailure"] = cleanup;
            }
            if (_failure == null) _closed.TrySetResult();
            else _closed.TrySetException(_failure);
        }
    }

    private void Commit(LiteDatabase db, List<Pending> batch)
    {
        var committing = false;
        try
        {
            if (!db.BeginTrans()) throw new InvalidOperationException("Writer already owns a transaction.");
            var rows = db.GetCollection("rows");
            foreach (var pending in batch) rows.Insert(pending.Request.Document());
            committing = true;
            if (!db.Commit()) throw new IOException("Commit did not confirm an owned transaction.");
        }
        catch (Exception error)
        {
            if (committing) throw new OutcomeUnknownException(error);
            // Only a duplicate-key error is a known, recoverable input rejection.
            // I/O, disposal and rollback failures stop the controller conservatively.
            if (error is not LiteException lite || lite.ErrorCode != LiteException.INDEX_DUPLICATE_KEY)
                throw new OutcomeUnknownException(error);
            try { db.Rollback(); }
            catch (Exception rollback) { throw new OutcomeUnknownException(new AggregateException(error, rollback)); }
            foreach (var pending in batch) pending.Completion.TrySetException(new BatchRejectedException(error));
            return;
        }
        Interlocked.Increment(ref Commits);
        Interlocked.Add(ref Completed, batch.Count);
        LargestBatch = Math.Max(LargestBatch, batch.Count);
        foreach (var pending in batch) pending.Completion.TrySetResult();
    }

    public void Dispose()
    {
        if (Thread.CurrentThread == _worker) throw new InvalidOperationException("Cannot close on writer thread.");
        _queue.CompleteAdding();
        _closed.Task.GetAwaiter().GetResult();
        _worker.Join();
    }
}
