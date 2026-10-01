using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteDB.Client.Shared;

namespace LiteDB
{
    public partial class SharedEngine
    {
        /// <summary>How long Dispose waits for admitted calls of other threads to return.</summary>
        internal static readonly TimeSpan DisposeCallWait = TimeSpan.FromSeconds(10);

        // Calls admitted to the engine (they own the mutex and passed the disposed check)
        // that have not returned yet, per thread. Guarded by _useLock.
        private readonly Dictionary<int, int> _admitted = new Dictionary<int, int>();
        private int _admittedCalls;
        // A last-reader close drains outside _useLock. Fresh users wait for its
        // publication to retire before opening/counting a replacement core.
        private Engine.LiteEngine _closingCore;

        // Synchronous callbacks can enter another facade for the same native
        // namespace. Keep only executing calls, not idle owners or leased readers.
        [ThreadStatic] private static List<SharedEngine> _executingCalls;

        internal readonly struct CallbackScope : IDisposable
        {
            private readonly bool _entered;
            internal CallbackScope(SharedEngine engine)
            {
                _entered = engine != null;
                if (!_entered) return;
                var calls = _executingCalls ?? (_executingCalls = new List<SharedEngine>());
                calls.Add(engine);
            }
            public void Dispose()
            {
                // RemoveAt clears the reference as well as restoring nested scopes.
                if (_entered) _executingCalls.RemoveAt(_executingCalls.Count - 1);
            }
        }

        private bool CannotWaitForOwnershipOnCurrentThread() =>
            _owner.IsOwnedByCurrentThread || _pin?.IsHeldByCurrentThread == true ||
            this.IsExecutingOwnedCoreOnCurrentThread();

        private void ThrowIfCallbackOwnershipWait()
        {
            // A handle acquires through a different child engine, so even this
            // facade's retaining frame cannot supply ordinary-call recursion.
            // Include close/retirement callbacks outside a public Call or Read.
            if (SharedCallFrames.RetainedByOther(_mutexName, connection: null))
                throw new InvalidOperationException("Cannot open a transaction handle from inside an operation retaining its shared writer ownership.");
            var calls = _executingCalls;
            if (calls == null) return;
            for (var i = calls.Count - 1; i >= 0; i--)
            {
                var caller = calls[i];
                if (StringComparer.Ordinal.Equals(caller._mutexName, _mutexName) &&
                    caller.CannotWaitForOwnershipOnCurrentThread())
                    throw new InvalidOperationException("Cannot open a transaction handle from inside an operation retaining its shared writer ownership.");
            }
        }
        private Func<bool> _callRetains;

        /// <summary>
        /// Under _useLock, with the mutex owned: refuse a call once Dispose started, else count
        /// it. Dispose closes the engine only after every counted call of another thread
        /// returned. An engine closed under a live call can neither dispose its busy page
        /// cache (the call's pinned or writable pages leak) nor finish the call's transaction.
        /// </summary>
        private void AdmitLocked()
        {
            while (_closingCore != null)
            {
                if (_closingCore.IsExecutingOnCurrentThread)
                    throw new InvalidOperationException("Cannot reenter a shared core while this reader is closing it.");
                if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(SharedEngine));
                Monitor.Wait(_useLock);
            }
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(SharedEngine));
            var thread = Environment.CurrentManagedThreadId;
            _admitted.TryGetValue(thread, out var depth);
            _admitted[thread] = depth + 1;
            _admittedCalls++;
        }

        private int AdmittedDepth()
        {
            lock (_useLock) return _admitted.TryGetValue(Environment.CurrentManagedThreadId, out var depth) ? depth : 0;
        }

        // User callbacks and custom streams may open readers that escape the call.
        private bool CanScope => _settings.ReadTransform == null && _settings.DataStream == null &&
            _settings.LogStream == null && _settings.TempStream == null;

        private T QueryDatabase<T>(Func<T> Query) => this.Call(() =>
        {
            var use = OpenDatabase(scoped: this.CanScope);
            try
            {
                return Query();
            }
            finally
            {
                CloseDatabase(use);
            }
        });

        /// <summary>Run a public call; the admission it made, if any, ends when it returns.</summary>
        private T Call<T>(Func<T> call, bool rollback = false)
        {
            // A late reader callback must be refused before waiting for either
            // connection bookkeeping or writer ownership that close is draining.
            if (Volatile.Read(ref _disposed) != 0)
            {
                if (rollback) return default;
                throw new ObjectDisposedException(nameof(SharedEngine));
            }
            if (this.IsForeignReaderCallback())
            {
                // A handed-off reader can still be running when its original owner
                // exits. Its callback cannot wait for the native owner draining it.
                // Unrelated new callers retain their normal admission wait/recovery.
                if (rollback) return default;
                throw new InvalidOperationException("Cannot wait for shared ownership from inside a reader executing on another ownership thread.");
            }
            using var callback = new CallbackScope(this);
            var depth = this.AdmittedDepth();
            var frame = this.OwnershipFrame(this.CallRetains);
            try
            {
                return call();
            }
            finally
            {
                frame.Dispose();
                this.EndAdmissions(depth);
            }
        }

        private Func<bool> CallRetains => _callRetains ?? (_callRetains = this.RetainsOwnershipOnCurrentThread);

        /// <summary>
        /// Whether a call of this connection executing on the current thread keeps the native
        /// mutex: its ownership belongs to this thread, or this thread's operation is inside
        /// the pin. Either ends only after that call, and any callback it runs, returns.
        /// </summary>
        private bool RetainsOwnershipOnCurrentThread()
        {
            if (_owner.IsOwnedByCurrentThread) return true;
            var pin = _pin;
            return pin != null && pin.IsOperatingOn(Thread.CurrentThread);
        }

        // A holder thread owns the OS mutex until the close it runs has returned.
        private static readonly Func<bool> HolderRetains = () => true;

        /// <summary>
        /// Frame for work outside a public call that can run user code (a caller stream while
        /// an engine closes) under the mutex; <paramref name="retains"/> tells whether it still holds it.
        /// </summary>
        private SharedCallFrames.Scope OwnershipFrame(Func<bool> retains) =>
            SharedCallFrames.Enter(_mutexName, this, retains);

        /// <summary>
        /// A reader streaming under the ownership of <paramref name="use"/>, or else of the
        /// connection's ownership <paramref name="generation"/>, which it keeps until disposed.
        /// </summary>
        private SharedDataReader RetainingReader(IBsonDataReader reader, Action dispose, SharedMutexPin use, int generation)
        {
            Func<bool> retains = use != null
                ? () => ReferenceEquals(_pin, use)
                : (Func<bool>)(() => _owner.Generation == generation);
            return new SharedDataReader(reader, dispose, _mutexName, this, retains);
        }

        /// <summary>
        /// Before any blocking acquisition of the native mutex: refuse when a call or reader of
        /// another connection to this database retains the mutex on this thread, for example
        /// when its input sequence or ReadTransform callback calls this connection. The wait
        /// could never end, because that ownership is released only after the callback returns.
        /// An idle owner (a reader, pin or transaction between calls) is not in a frame and is
        /// still waited for: a pin ends for the waiter and a result may be disposed on any thread.
        /// An explicit transaction completes only on its own thread (#3073).
        /// </summary>
        private void ThrowIfCallerRetainsOwnership()
        {
            if (!SharedCallFrames.RetainedByOther(_mutexName, this)) return;
            throw new InvalidOperationException(
                "Cannot wait for shared-mode ownership of this database from inside an operation of another " +
                "connection to it that holds the ownership on this thread, such as its input sequence or " +
                "ReadTransform callback. Use that connection for nested operations, or run them after it returns.");
        }

        private void EndAdmissions(int depth)
        {
            var thread = Environment.CurrentManagedThreadId;
            lock (_useLock)
            {
                if (!_admitted.TryGetValue(thread, out var current) || current <= depth) return;
                _admittedCalls -= current - depth;
                if (depth == 0) _admitted.Remove(thread);
                else _admitted[thread] = depth;
                Monitor.PulseAll(_useLock);
            }
        }

        /// <summary>
        /// Dispose's drain: wait until no other thread's admitted call is running. Calls of the
        /// disposing thread itself (Dispose from inside an operation) are not waited for. The
        /// wait is bounded: a call blocked on something only this Dispose would end (another
        /// thread's explicit transaction holding an engine lock) must not hang it; past the
        /// bound Dispose proceeds, and that call fails on the closed engine.
        /// </summary>
        private void WaitForAdmittedCalls()
        {
            var waited = Stopwatch.StartNew();
            var thread = Environment.CurrentManagedThreadId;
            lock (_useLock)
            {
                while (true)
                {
                    _admitted.TryGetValue(thread, out var own);
                    if (_admittedCalls - own <= 0) return;
                    var remaining = DisposeCallWait - waited.Elapsed;
                    if (remaining <= TimeSpan.Zero) return;
                    Monitor.Wait(_useLock, remaining);
                }
            }
        }
    }
}
