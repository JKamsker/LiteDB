using System;
using System.Collections.Generic;
using LiteDB.Tests.Concurrency.LifetimeModel.Direct;

namespace LiteDB.Tests.Concurrency.LifetimeModel.Pr133
{
    /// <summary>
    /// The Direct engine's maintenance paths of PR #133 at the modeled commit: <c>LiteEngine.Close</c>
    /// behind <c>OperationLifetime.Exclusive</c> (LiteEngine.cs:236-264), <c>Rebuild</c>
    /// (Rebuild.cs:19-71) and the fatal stop that defers its close to the last lease
    /// (EngineState.cs BeginStop/CompleteStop, LiteEngine.StopAfterOperations, Close(ex), LiteEngine.cs:288-314).
    /// </summary>
    internal sealed class Pr133Lifecycle
    {
        private readonly Pr133EngineModel _engine;

        public Pr133Lifecycle(Pr133EngineModel engine) => _engine = engine;

        private LifetimeLedger Ledger => _engine.Ledger;

        public IEnumerable<Step> Run(ModelThread t, OpKind kind)
        {
            switch (kind)
            {
                case OpKind.Close: return this.Dispose(t);
                case OpKind.Rebuild: return this.Rebuild(t);
                case OpKind.Fatal: return this.Fatal(t);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private IEnumerable<Step> Dispose(ModelThread t)
        {
            var op = this.Ledger.Begin(t, OpKind.Close, "LiteEngine.Dispose");
            foreach (var step in this.Close(t, user: true)) yield return step;
            if (t.Fault != null) { this.Ledger.End(op, OpOutcome.Rejected, t.Fault, "LiteEngine.cs:238"); t.Fault = null; yield break; }
            this.Ledger.End(op, OpOutcome.Completed, null, "LiteEngine.cs Dispose returns");
        }

        /// <summary>LiteEngine.Close(checkpoint: true) (LiteEngine.cs:236-264).</summary>
        private IEnumerable<Step> Close(ModelThread t, bool user)
        {
            yield return Step.At(_engine.Revision == "0d5e5effa"
                ? "LiteEngine.cs:238 Close: OperationLifetime.Exclusive(() => true, stopWaiters: LockService.StopWaiters, closing: true)"
                : "LiteEngine.cs:238 Close: OperationLifetime.Exclusive(() => true, stopWaiters: LockService.StopWaiters)");
            var owned = new Ref<bool>();
            foreach (var step in _engine.Operations.TakeExclusive(t, () => true, null, () => _engine.Locker.StopWaiters(), owned, closing: true)) yield return step;
            if (t.Fault != null) yield break;

            yield return Step.At("LiteEngine.cs:239 if (_state.Disposed) return");
            var state = _engine.State;
            if (state.Disposed)
            {
                if (user) this.Ledger.FenceAcquired("connection", "LiteEngine.cs:239 Dispose returned early (already disposed)");
            }
            else
            {
                yield return Step.At("LiteEngine.cs:241 _state.Disposed = true");
                state.Disposed = true;
                this.Ledger.DoomTransactions("LiteEngine.cs:241");

                yield return Step.At("LiteEngine.cs:246 TransactionMonitor.Dispose");
                var monitor = _engine.Monitor;
                foreach (var step in this.DisposeMonitor(t, monitor, user)) yield return step;

                if (_engine.LogHasContent)
                {
                    yield return Step.At("LiteEngine.cs:251 TryCloseCheckpoint: LockService.TryEnterExclusive (10 ms)");
                    var locker = monitor.Locker;
                    if (locker.Writer != t.Id && !locker.Readers.ContainsKey(t.Id))
                    {
                        foreach (var step in locker.EnterWriteLock(t, Pr133EngineModel.ReaderWait, "checkpoint skipped")) yield return step;
                        if (t.Fault == null)
                        {
                            yield return Step.At("WalIndexService.Checkpoint.cs checkpoint under exclusive, then ExitExclusive");
                            locker.ExitWriteLock(t);
                        }
                        t.Fault = null;
                    }
                }

                yield return Step.At("LiteEngine.cs:261 LockService.Dispose");
                _engine.Locker.Dispose();
            }

            if (owned.Value)
            {
                yield return Step.At("LiteEngine.cs:238 exclusive lease Dispose: OperationLifetime.Exit");
                foreach (var step in _engine.Operations.Exit(t, exclusive: true)) yield return step;
            }
        }

        private IEnumerable<Step> DisposeMonitor(ModelThread t, TransactionMonitorModel monitor, bool user)
        {
            var owned = new List<TxnModel>();
            foreach (var transaction in monitor.Registry)
            {
                if (transaction.Owner == t.Id && transaction.LockedCollection != null) owned.Add(transaction);
            }
            foreach (var step in monitor.Dispose(t, this.Ledger, user)) yield return step;
            foreach (var transaction in owned) monitor.Locker.ExitLock(t, transaction.LockedCollection);
        }

        /// <summary>LiteEngine.Rebuild (Rebuild.cs:19-71 at the modeled commit).</summary>
        private IEnumerable<Step> Rebuild(ModelThread t)
        {
            var op = this.Ledger.Begin(t, OpKind.Rebuild, "LiteEngine.Rebuild");
            yield return Step.At("Rebuild.cs:22 if (_locker.IsInTransaction) throw");
            if (_engine.Locker.IsInTransaction(t)) { this.Ledger.End(op, OpOutcome.Rejected, Faults.AlreadyInTransaction, "Rebuild.cs:22"); yield break; }

            yield return Step.At("Rebuild.cs:26 OperationLifetime.Exclusive(() => _locker.TransactionsCount == 0, pragma timeout)");
            var owned = new Ref<bool>();
            foreach (var step in _engine.Operations.TakeExclusive(t, () => _engine.Locker.ReaderCount == 0, Pr133EngineModel.PragmaTimeout, null, owned))
                yield return step;
            if (t.Fault != null) { this.Ledger.End(op, OpOutcome.Rejected, t.Fault, "Rebuild.cs:26"); t.Fault = null; yield break; }

            yield return Step.At("Rebuild.cs:41-42 LockService.EnterExclusive");
            var locker = _engine.Locker;
            if (locker.Writer != t.Id) foreach (var step in locker.EnterWriteLock(t, Pr133EngineModel.PragmaTimeout, Faults.ExclusiveTimeout)) yield return step;
            if (t.Fault != null)
            {
                this.Ledger.End(op, OpOutcome.Rejected, t.Fault, "Rebuild.cs:42");
                t.Fault = null;
            }
            else
            {
                this.Ledger.Admitted(op, "Rebuild.cs:42 exclusive admission", $"gen{locker.Generation}");
                yield return Step.At("Rebuild.cs:44 this.Close(releaseMode: false)");
                foreach (var step in this.Close(t, user: false)) yield return step;
                yield return Step.At("Rebuild.cs:51 RebuildService.Rebuild replaces the files");

                yield return Step.At("Rebuild.cs:71 Open: LiteEngine.cs:115 _state = new EngineState");
                _engine.State = new EngineStateModel();
                yield return Step.At("LiteEngine.cs:181 _locker = new LockService");
                _engine.OpenLocker();
                yield return Step.At("LiteEngine.cs:200 _monitor = new TransactionMonitor");
                _engine.OpenMonitor();
                yield return Step.At("Rebuild.cs:73 _state.Disposed = false");
                _engine.State.Disposed = false;
                this.Ledger.End(op, OpOutcome.Completed, null, "Rebuild.cs:75 returns");
            }

            yield return Step.At("Rebuild.cs:26 maintenance lease Dispose: OperationLifetime.Exit");
            foreach (var step in _engine.Operations.Exit(t, exclusive: true)) yield return step;
        }

        /// <summary>An auto-transaction write whose commit fails with an I/O error: the engine stops.</summary>
        private IEnumerable<Step> Fatal(ModelThread t)
        {
            var op = this.Ledger.Begin(t, OpKind.Fatal, "Insert whose commit fails with an I/O error");
            foreach (var step in _engine.PublicWrite(t, op, "c1", injectFailure: true)) yield return step;
            if (!op.Ended) this.Ledger.End(op, OpOutcome.Completed, null, "Insert returns");
        }

        /// <summary>
        /// EngineState.Stop: BeginStop publishes the failure; CompleteStop calls
        /// LiteEngine.StopAfterOperations, which hands Close(ex, origin) to OperationLifetime.Stop.
        /// </summary>
        public IEnumerable<Step> Stop(ModelThread t, string fault)
        {
            yield return Step.At("EngineState.cs:86 BeginStop");
            var state = _engine.State;
            if (state.Disposed || state.Failure != null) yield break;
            state.Failure = fault;
            this.Ledger.DoomTransactions("EngineState.cs failure published");

            yield return Step.At("EngineState.cs:101 CompleteStop: LiteEngine.StopAfterOperations (LiteEngine.cs:35): OperationLifetime.Stop");
            foreach (var step in _engine.Operations.Stop(t, closer => this.CloseAfterFailure(closer, state))) yield return step;
        }

        /// <summary>LiteEngine.Close(ex, origin) (LiteEngine.cs:288-314 at the modeled commit).</summary>
        private IEnumerable<Step> CloseAfterFailure(ModelThread t, EngineStateModel origin)
        {
            yield return Step.At("LiteEngine.cs:290-293 Close(ex, origin): origin and disposed checks");
            if (origin != _engine.State || origin.Disposed) yield break;
            origin.Disposed = true;
            yield return Step.At("LiteEngine.cs:297 TransactionMonitor.Dispose");
            foreach (var step in this.DisposeMonitor(t, _engine.Monitor, user: false)) yield return step;
            yield return Step.At("LiteEngine.cs:312 LockService.Dispose");
            _engine.Locker.Dispose();
        }
    }
}
