using System;
using System.Collections.Concurrent;
using System.Threading;
using LiteDB.Client.Shared;
using LiteDB.Engine;

namespace LiteDB
{
    public partial class SharedEngine
    {
        // Cached entries are inert managed metadata: they own no native admission or files.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> TransactionWriters =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        internal TransactionResources OpenTransactionResources(CancellationToken closing, object sessionToken)
        {
            // A caller stream can capture the facade; a native holder must not root that
            // graph indefinitely, or perform storage I/O after its external owner is gone.
            if (_settings.DataStream != null || _settings.LogStream != null || _settings.TempStream != null)
                throw new NotSupportedException("Shared transaction handles require filename-backed storage without caller streams.");
            lock (_useLock)
            {
                if (_disposed != 0) throw new ObjectDisposedException(nameof(SharedEngine));
                if (_transactionRunning && _owner.IsOwnedByCurrentThread)
                    throw new InvalidOperationException("Complete the legacy transaction before opening a transaction handle.");
            }
            var name = SharedMutexNameFactory.Create(_settings.Filename, _settings.SharedMutexNameStrategy);
            var gate = TransactionWriters.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
            // Pending begins use their caller's synchronous wait, never a holder thread/engine.
            gate.Wait(closing);
            TransactionHolder holder;
            var policyAnchor = _settings.ReadTransform;
            try
            {
                closing.ThrowIfCancellationRequested();
                var settings = _settings.Clone();
                // The holder thread must not root application callbacks that can capture the
                // facade/handle. The external resource owner retains the delegate while live.
                if (policyAnchor != null)
                {
                    var callback = new WeakReference<Func<string, BsonValue, BsonValue>>(policyAnchor);
                    settings.ReadTransform = (collection, value) => callback.TryGetTarget(out var transform)
                        ? transform(collection, value) : throw new ObjectDisposedException("Transaction read policy");
                }
                settings.CoordinationSignals = null;
                settings.SharedFileHandles = null;
                holder = new TransactionHolder(new SharedEngine(settings), gate, closing, sessionToken);
            }
            catch { gate.Release(); throw; }
            return holder.Open(policyAnchor);
        }

        /// <summary>One internal native owner per database, independent of application threads.</summary>
        private sealed class TransactionHolder
        {
            private readonly SharedEngine _child;
            private readonly SemaphoreSlim _gate;
            private readonly CancellationToken _closing;
            private readonly object _sessionToken;
            private readonly ManualResetEventSlim _opened = new ManualResetEventSlim();
            private readonly ManualResetEventSlim _close = new ManualResetEventSlim();
            private Thread _thread;
            private Exception _error;
            private LiteEngine _engine;
#if DEBUG || TESTING
            private readonly Func<string, bool, Action<string>> _streamProbe = NativeAdmissionStreamProbe.Attach;
#endif

            internal TransactionHolder(SharedEngine child, SemaphoreSlim gate, CancellationToken closing, object sessionToken)
            { _child = child; _gate = gate; _closing = closing; _sessionToken = sessionToken; }

            internal TransactionResources Open(object policyAnchor)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "LiteDB transaction mutex" };
                try { _thread.Start(); }
                catch (Exception error)
                {
                    try { _child.Dispose(); }
                    catch (Exception cleanup) { error.Data["LiteDB.TransactionOpenCleanup"] = cleanup; }
                    finally { _gate.Release(); _opened.Dispose(); _close.Dispose(); }
                    throw;
                }
                _opened.Wait();
                if (_error != null) { Release(); throw _error; }
                return new TransactionResources(_engine, _engine.CurrentContext, Release, () => _close.Set(), policyAnchor);
            }

            private void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception error)
                {
                    if (_error == null) _error = error;
                    else _error.Data["LiteDB.SharedCleanup." + _error.Data.Count] = error;
                }
            }

            private void Run()
            {
                using var dependency = new SessionCloseDependency(_sessionToken);
#if DEBUG || TESTING
                NativeAdmissionStreamProbe.Attach = _streamProbe;
#endif
                var acquired = false;
                try
                {
                    _child.OpenDatabase(scoped: true, writing: !_child._settings.ReadOnly, closing: _closing);
                    acquired = true;
                    _closing.ThrowIfCancellationRequested();
                    _engine = _child._engine;
                    _opened.Set();
                    _close.Wait();
                }
                catch (Exception error) { _error = error; }
                finally
                {
                    if (acquired) Cleanup(() => _child.CloseDatabase(reportErrors: true));
                    Cleanup(() => _child.EndAdmissions(0));
                    Cleanup(_child.Dispose);
                    _gate.Release();
                    // Failed-open publication follows all cleanup and preserves its original error.
                    _opened.Set();
                }
            }

            private void Release()
            {
                _close.Set();
                _thread.Join();
                _opened.Dispose();
                _close.Dispose();
                if (_error != null) throw _error;
            }
        }
    }
}
