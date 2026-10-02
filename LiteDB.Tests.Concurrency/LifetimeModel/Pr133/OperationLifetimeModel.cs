using System;
using System.Collections.Generic;

namespace LiteDB.Tests.Concurrency.LifetimeModel.Pr133
{
    /// <summary>
    /// <c>OperationLifetime</c> (LiteDB/Engine/Services/OperationLifetime.cs) as it exists at the
    /// modeled PR #133 commit: operation leases per thread, one exclusive maintenance owner, a
    /// count of queued maintenance owners that fences new work, and a close deferred until the
    /// last lease exits. Line numbers refer to that commit's file. At <c>0d5e5effa</c> a close
    /// marks itself requested (Exclusive(closing: true), :91-95) and Enter refuses fresh work with
    /// ENGINE_DISPOSED instead of waiting behind it (:38-40); at <c>cb36c346e</c> neither exists.
    /// </summary>
    internal sealed class OperationLifetimeModel
    {
        private readonly Dictionary<int, int> _threads = new Dictionary<int, int>();
        private readonly bool _refuseFreshWhileClosing;

        public OperationLifetimeModel(bool refuseFreshWhileClosing) => _refuseFreshWhileClosing = refuseFreshWhileClosing;

        /// <summary>_closingRequested (0d5e5effa only).</summary>
        public bool ClosingRequested { get; private set; }

        private string Line(int knownBad, int fix) => (_refuseFreshWhileClosing ? fix : knownBad).ToString();

        public int Active { get; private set; }

        public int? Exclusive { get; private set; }

        public int WaitingExclusive { get; private set; }

        /// <summary>_deferredClose: the close a fatal stop left for the last exiting lease.</summary>
        public Func<ModelThread, IEnumerable<Step>> DeferredClose { get; private set; }

        public bool HoldsLease(ModelThread t) => _threads.ContainsKey(t.Id);

        /// <summary>
        /// OperationLifetime.Enter (:23-44): wait (Monitor.Wait, no bound) while another thread
        /// holds maintenance, or while maintenance is queued and this is fresh work: not a
        /// continuation, no lease on this thread, and the thread owns no transaction.
        /// </summary>
        public IEnumerable<Step> Enter(ModelThread t, bool continuation, Func<bool> ownsTransaction)
        {
            bool Fresh() => !continuation && !_threads.ContainsKey(t.Id) && !ownsTransaction();
            bool Blocked() => (this.Exclusive != null && this.Exclusive != t.Id) || (this.Exclusive == null && this.WaitingExclusive != 0 && Fresh());
            bool Refused() => _refuseFreshWhileClosing && this.ClosingRequested && Fresh();
            yield return Step.Wait($"OperationLifetime.cs:{this.Line(30, 31)} Enter waits while maintenance holds or is queued (Monitor.Wait, unbounded)",
                () => !Blocked() || Refused());
            if (Blocked())
            {
                t.Fault = "LiteException(ENGINE_DISPOSED)"; // 0d5e5effa :38-40
                yield break;
            }
            _threads[t.Id] = _threads.TryGetValue(t.Id, out var count) ? count + 1 : 1;
            this.Active++;
        }

        /// <summary>OperationLifetime.Exit (:46-66) and FinishClose (:68-73): the last lease runs a deferred close.</summary>
        public IEnumerable<Step> Exit(ModelThread t, bool exclusive)
        {
            Func<ModelThread, IEnumerable<Step>> close = null;
            if (exclusive) this.Exclusive = null;
            else
            {
                this.Active--;
                if (--_threads[t.Id] == 0) _threads.Remove(t.Id);
            }
            if (this.Active == 0 && this.Exclusive == null && this.DeferredClose != null)
            {
                close = this.DeferredClose;
                this.DeferredClose = null;
                this.Exclusive = t.Id;
            }
            if (close == null) yield break;
            yield return Step.At($"OperationLifetime.cs:{this.Line(71, 78)} FinishClose runs the deferred close");
            foreach (var step in close(t)) yield return step;
            this.Exclusive = null; // FinishClose finally
        }

        /// <summary>
        /// OperationLifetime.Exclusive (:75-108). Sets <paramref name="owned"/> when this call took the
        /// exclusive lease (a thread that already holds it gets the default lease, :81). The real loop
        /// re-checks every 10 ms and calls <paramref name="stopWaiters"/> whenever no other owner holds
        /// maintenance; the model waits for the same conditions. A bounded wait applies its timeout to
        /// each of the two waits below (the real deadline covers both; they only differ when another
        /// maintenance owner holds the lease first).
        /// </summary>
        public IEnumerable<Step> TakeExclusive(ModelThread t, Func<bool> drained, int? timeout, Action stopWaiters, Ref<bool> owned, bool closing = false)
        {
            owned.Value = false;
            if (this.Exclusive == t.Id) yield break; // :81 / :88
            if (_threads.ContainsKey(t.Id))
            {
                t.Fault = "InvalidOperationException(close or rebuild inside an executing engine operation)";
                yield break;
            }
            if (closing && _refuseFreshWhileClosing) this.ClosingRequested = true; // 0d5e5effa :91-95
            this.WaitingExclusive++;
            while (true)
            {
                yield return Wait($"OperationLifetime.cs:{this.Line(90, 102)} Exclusive waits for another maintenance owner", timeout, () => this.Exclusive == null);
                if (t.TimedOut) break;
                stopWaiters?.Invoke();
                yield return Wait($"OperationLifetime.cs:{this.Line(93, 105)} Exclusive waits for active operations and dependencies to drain", timeout,
                    () => this.Exclusive != null || (this.Active == 0 && drained()));
                if (t.TimedOut || this.Exclusive == null) break;
            }
            this.WaitingExclusive--; // finally
            if (t.TimedOut)
            {
                t.Fault = "LiteException(LOCK_TIMEOUT operation/maintenance)";
                yield break;
            }
            this.Exclusive = t.Id;
            owned.Value = true;
        }

        /// <summary>OperationLifetime.Stop (:110-118): close now, or defer it to the last lease.</summary>
        public IEnumerable<Step> Stop(ModelThread t, Func<ModelThread, IEnumerable<Step>> close)
        {
            if (this.Active != 0 || this.Exclusive != null)
            {
                this.DeferredClose = close;
                yield break;
            }
            this.Exclusive = t.Id;
            foreach (var step in close(t)) yield return step;
            this.Exclusive = null; // FinishClose :72
        }

        private static Step Wait(string site, int? timeout, Func<bool> ready) =>
            timeout.HasValue ? Step.TimedWait(site, timeout.Value, ready) : Step.Wait(site, ready);

        public override string ToString() =>
            $"operations(active={this.Active}, exclusive={this.Exclusive?.ToString() ?? "-"}, waitingExclusive={this.WaitingExclusive}" +
            $"{(this.DeferredClose != null ? ", close deferred" : "")})";
    }
}
