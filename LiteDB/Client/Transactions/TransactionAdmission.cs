using System;
using System.Diagnostics;
using System.Threading;
using LiteDB.Client.Shared;
#if DEBUG || TESTING
using LiteDB.Utils;
#endif

namespace LiteDB
{
    /// <summary>A begin-only budget shared by local queuing and native writer admission.</summary>
    internal sealed class TransactionAdmission : IDisposable
    {
        private CancellationToken _caller, _closing, _wait;
        private CancellationTokenSource _linked;
        private readonly TimeSpan _timeout;
        private readonly long _started;
#if DEBUG || TESTING
        internal static Action<string> Observe;
        internal static Func<long> TimestampOverride;
#endif
        private static long Timestamp()
        {
#if DEBUG || TESTING
            LiteDB.Utils.Reachability.FaultPoint("TimestampOverride");
            if (TimestampOverride != null) return TimestampOverride();
#endif
            return Stopwatch.GetTimestamp();
        }

        internal static void Validate(TimeSpan timeout)
        {
            if (timeout != Timeout.InfiniteTimeSpan && (timeout < TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(timeout), "Use a nonnegative admission timeout up to Int32.MaxValue milliseconds, or Timeout.InfiniteTimeSpan.");
        }

        internal TransactionAdmission(TimeSpan timeout, CancellationToken caller, CancellationToken closing)
        {
            Validate(timeout);
            _started = Timestamp();
            _timeout = timeout;
            _caller = caller;
            _closing = closing;
            if (caller.CanBeCanceled && closing.CanBeCanceled)
                _wait = (_linked = CancellationTokenSource.CreateLinkedTokenSource(caller, closing)).Token;
            else _wait = caller.CanBeCanceled ? caller : closing;
        }

        internal void CheckCancellation()
        {
            // Preserve the caller's token rather than leaking the internal linked token.
            _caller.ThrowIfCancellationRequested();
            _closing.ThrowIfCancellationRequested();
        }

        private int RemainingMilliseconds
        {
            get
            {
                if (_timeout == Timeout.InfiniteTimeSpan) return Timeout.Infinite;
                var remaining = _timeout.TotalMilliseconds - (Timestamp() - _started) * 1000d / Stopwatch.Frequency;
                return remaining <= 0 ? 0 : (int)Math.Min(int.MaxValue, Math.Ceiling(remaining));
            }
        }

        private void Expired() => throw new TimeoutException("Shared transaction admission timed out while waiting for writer ownership.");

        internal void WaitLocal(SemaphoreSlim gate)
        {
            CheckCancellation();
#if DEBUG || TESTING
            Observe?.Invoke("local-wait");
#endif
            var remaining = RemainingMilliseconds;
            if (_timeout > TimeSpan.Zero && remaining == 0) Expired();
            try
            {
#if DEBUG || TESTING
                using (WaitGraph.Wait(WaitGraph.Of(gate, "shared-handle-writer", WaitPrimitive.SemaphoreSlim), this.GraphBound,
                    "TransactionAdmission.WaitLocal"))
#endif
                if (!gate.Wait(remaining, _wait)) Expired();
            }
            catch (OperationCanceledException) { CheckCancellation(); throw; }
            // The caller now owns the gate and must release it if a later check fails.
        }

        internal bool EnterNative(SharedMutexOwner owner, bool scoped)
        {
#if DEBUG || TESTING
            Observe?.Invoke("native-wait");
#endif
#if DEBUG || TESTING
            // A poll for the connection's ownership and the OS writer mutex, on the holder thread.
            using (WaitGraph.Wait(owner.GraphOwnership, this.GraphBound, "TransactionAdmission.EnterNative", also: owner.GraphMutexResource))
#endif
            while (true)
            {
                CheckCancellation();
                var remaining = RemainingMilliseconds;
                if (_timeout > TimeSpan.Zero && remaining == 0) Expired();
                if (owner.TryEnter(out var abandoned, scoped)) return abandoned;
                if (remaining == 0) Expired();
#if DEBUG || TESTING
                WaitGraph.Recheck();
#endif
                _wait.WaitHandle.WaitOne(remaining < 0 ? 10 : Math.Min(10, remaining));
            }
        }

#if DEBUG || TESTING
        /// <summary>Proof overlay (PR #133): one budget bounds both waits; else caller/close cancellation; else none.</summary>
        private WaitBound GraphBound => _timeout != Timeout.InfiniteTimeSpan ? WaitBound.After(_timeout)
            : _wait.CanBeCanceled ? WaitBound.Cancellation : WaitBound.Unbounded;

#endif
        internal void Acquired(string stage)
        {
#if DEBUG || TESTING
            Observe?.Invoke(stage);
#endif
            CheckCancellation();
        }

        public void Dispose()
        {
            // Admission ends before a handle is returned. Even a stale stack reference
            // must not retain caller cancellation callbacks (which may capture the facade).
            _linked?.Dispose();
            _linked = null;
            _caller = _closing = _wait = default;
        }
    }
}
