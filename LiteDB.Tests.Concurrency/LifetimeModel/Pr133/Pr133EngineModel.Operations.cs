using System;
using System.Collections.Generic;
using LiteDB.Tests.Concurrency.LifetimeModel.Direct;

namespace LiteDB.Tests.Concurrency.LifetimeModel.Pr133
{
    public sealed partial class Pr133EngineModel
    {
        /// <summary>
        /// A public engine call: LiteEngine.EnterPublicOperation (LiteEngine.cs:33-34) or, for a reader's
        /// advance and dispose, EngineState.EnterOperation (EngineState.cs:22, continuation), around
        /// <paramref name="body"/>; the lease's Dispose exits it (and may run a deferred close).
        /// </summary>
        private IEnumerable<Step> Public(ModelThread t, ModelOp op, Func<IEnumerable<Step>> body, bool continuation = false)
        {
            yield return Step.At(continuation
                ? "BsonDataReader.cs:111/161 EngineState.EnterOperation (continuation lease)"
                : "LiteEngine.cs:34 EnterPublicOperation: OperationLifetime.Enter");
            foreach (var step in this.Operations.Enter(t, continuation, () => this.Locker.IsInTransaction(t))) yield return step;
            if (t.Fault != null)
            {
                this.Reject(op, t.Fault, "OperationLifetime.Enter refuses fresh work while a close is requested");
                t.Fault = null;
                yield break;
            }
            foreach (var step in body()) yield return step;
            yield return Step.At("operation lease Dispose: OperationLifetime.Exit");
            foreach (var step in this.Operations.Exit(t, exclusive: false)) yield return step;
        }

        /// <summary>An Insert as a public call, for the fatal maintenance operation.</summary>
        internal IEnumerable<Step> PublicWrite(ModelThread t, ModelOp op, string collection, bool injectFailure) =>
            this.Public(t, op, () => this.AutoWrite(t, op, collection, null, injectFailure));

        private IEnumerable<Step> FreshProgram(ModelThread t, string collection)
        {
            var op = this.Ledger.Begin(t, OpKind.Fresh, $"Insert into {collection}");
            foreach (var step in this.Public(t, op, () => this.AutoWrite(t, op, collection, null, injectFailure: false))) yield return step;
            if (!op.Ended) this.Complete(op, "Insert returns");
        }

        private IEnumerable<Step> NestedProgram(ModelThread t, string collection)
        {
            var reader = new Ref<ReaderModel>();
            var outer = this.Ledger.Begin(t, OpKind.Fresh, "Query reader kept open around nested work");
            foreach (var step in this.Public(t, outer, () => this.OpenReader(t, outer, reader))) yield return step;
            if (outer.Ended) yield break;

            var nested = this.Ledger.Begin(t, OpKind.Nested, $"Insert into {collection} while this thread's reader is open");
            if (reader.Value.State == this.State && this.State.Valid) this.Ledger.TransactionLive(nested, true, "reader open on this thread");
            foreach (var step in this.Public(t, nested, () => this.AutoWrite(t, nested, collection, null, injectFailure: false))) yield return step;
            if (!nested.Ended) this.Complete(nested, "Insert returns");
            this.Ledger.TransactionLive(nested, false, "nested work returned");

            foreach (var step in this.DisposeReader(t, reader.Value)) yield return step;
        }

        private IEnumerable<Step> OwnerProgram(ModelThread t, string collection)
        {
            var op = this.Ledger.Begin(t, OpKind.Owner, $"BeginTrans, Insert into {collection}, Commit");
            foreach (var step in this.Public(t, op, () => this.BeginTrans(t, op))) yield return step;
            if (op.Ended) yield break;
            foreach (var step in this.Public(t, op, () => this.AutoWrite(t, op, collection, null, injectFailure: false))) yield return step;
            if (op.Ended) yield break;
            foreach (var step in this.Public(t, op, () => this.Commit(t, op))) yield return step;
        }

        /// <summary>LiteEngine.BeginTrans (Transaction.cs:16-35 at the modeled commit).</summary>
        private IEnumerable<Step> BeginTrans(ModelThread t, ModelOp op)
        {
            yield return Step.At("Transaction.cs:19 BeginTrans: _state.Validate()");
            if (!this.State.Valid) { this.Reject(op, this.State.Failure ?? Faults.EngineDisposed, "Transaction.cs:19"); yield break; }
            var monitor = this.Monitor;
            var transaction = new Ref<TxnModel>();
            var isNew = new Ref<bool>();
            yield return Step.At("Transaction.cs:23 TransactionMonitor.GetTransaction");
            foreach (var step in monitor.GetTransaction(t, false, PragmaTimeout, transaction, isNew)) yield return step;
            if (t.Fault != null) { this.Reject(op, t.Fault, "Transaction.cs:23"); t.Fault = null; yield break; }
            transaction.Value.Explicit = true;
            this.Ledger.Admitted(op, "Transaction.cs:27 explicit transaction begun", this.Scopes(monitor));
            if (this.State.Valid && !monitor.Disposed) this.Ledger.TransactionLive(op, true, "Transaction.cs:27");
        }

        /// <summary>LiteEngine.Commit (Transaction.cs:39-59 at the modeled commit).</summary>
        private IEnumerable<Step> Commit(ModelThread t, ModelOp op)
        {
            yield return Step.At("Transaction.cs:42 Commit: _state.Validate()");
            if (!this.State.Valid) { this.Reject(op, this.State.Failure ?? Faults.EngineDisposed, "Transaction.cs:42"); yield break; }
            var current = this.Monitor;
            yield return Step.At("Transaction.cs:44 GetTransactionForCompletion: GetTransaction(create: false)");
            if (current.Disposed) { this.Reject(op, Faults.MonitorDisposed, "TransactionMonitor.GetTransaction"); yield break; }
            if (!current.Slot.TryGetValue(t.Id, out var own))
            {
                this.Ledger.TransactionLive(op, false, "Commit: nothing to commit");
                this.Complete(op, "Commit returns false");
                yield break;
            }
            foreach (var step in this.CommitAndRelease(t, own, injectFailure: false)) yield return step;
            this.Ledger.TransactionLive(op, false, "committed or failed");
            if (t.Fault != null) { this.Fail(op, t.Fault, "Transaction.cs:53"); t.Fault = null; yield break; }
            this.Complete(op, "Commit returns true");
        }

        private IEnumerable<Step> ReaderOpenProgram(ModelThread t, Handoff<ReaderModel> handoff, bool sameThread)
        {
            var op = this.Ledger.Begin(t, OpKind.Continuation,
                sameThread ? "Query reader read and disposed later on this thread" : "Query reader handed to another thread");
            var reader = new Ref<ReaderModel>();
            foreach (var step in this.Public(t, op, () => this.OpenReader(t, op, reader))) yield return step;
            if (op.Ended) { handoff.Cancelled = true; yield break; }
            if (sameThread)
            {
                foreach (var step in this.DisposeReader(t, reader.Value)) yield return step;
                yield break;
            }
            handoff.Value = reader.Value;
            handoff.Started = true;
        }

        private IEnumerable<Step> ReaderDisposeProgram(ModelThread t, Handoff<ReaderModel> handoff)
        {
            yield return Step.Wait("user code: wait for the reader handed over", () => handoff.Started || handoff.Cancelled);
            if (handoff.Cancelled) yield break;
            foreach (var step in this.DisposeReader(t, handoff.Value)) yield return step;
        }

        private IEnumerable<Step> CallbackProgram(ModelThread t, string collection, Handoff<bool> dependency)
        {
            var op = this.Ledger.Begin(t, OpKind.CallbackDependency, $"Insert into {collection} whose lazy input waits for work on another thread");
            foreach (var step in this.Public(t, op, () => this.AutoWrite(t, op, collection, () => this.Callback(dependency), injectFailure: false))) yield return step;
            if (!dependency.Started) dependency.Cancelled = true;
            if (!op.Ended) this.Complete(op, "Insert returns");
        }

        private IEnumerable<Step> Callback(Handoff<bool> dependency)
        {
            yield return Step.At("user callback (Insert's lazy input): start fresh work on another thread");
            dependency.Started = true;
            yield return Step.Wait("user callback: blocks until the dependent operation returns (Task.Wait)", () => dependency.Finished);
        }

        private IEnumerable<Step> DependentProgram(ModelThread t, string collection, Handoff<bool> dependency)
        {
            yield return Step.Wait("dependent thread: waits for the callback's request", () => dependency.Started || dependency.Cancelled);
            if (dependency.Cancelled) yield break;
            var op = this.Ledger.Begin(t, OpKind.Fresh, $"Insert into {collection} requested by another operation's callback");
            foreach (var step in this.Public(t, op, () => this.AutoWrite(t, op, collection, null, injectFailure: false))) yield return step;
            if (!op.Ended) this.Complete(op, "Insert returns");
            dependency.Finished = true;
        }

        /// <summary>
        /// LiteEngine.ExecuteAutoTransaction (Transaction.cs:99-128) for an Insert: validate, get or
        /// join the thread transaction, take the collection's write lock (Snapshot), run the input
        /// <paramref name="callback"/>, and commit a transaction it created. Ends <paramref name="op"/> on
        /// any exception: rejected before admission, failed after it.
        /// </summary>
        internal IEnumerable<Step> AutoWrite(ModelThread t, ModelOp op, string collection, Func<IEnumerable<Step>> callback, bool injectFailure)
        {
            yield return Step.At("Transaction.cs:101 ExecuteAutoTransaction: _state.Validate()");
            if (!this.State.Valid) { this.Reject(op, this.State.Failure ?? Faults.EngineDisposed, "Transaction.cs:101"); yield break; }

            var monitor = this.Monitor;
            var transaction = new Ref<TxnModel>();
            var isNew = new Ref<bool>();
            yield return Step.At("Transaction.cs:105 TransactionMonitor.GetTransaction");
            foreach (var step in monitor.GetTransaction(t, false, PragmaTimeout, transaction, isNew)) yield return step;
            if (t.Fault != null) { this.Reject(op, t.Fault, "TransactionMonitor.GetTransaction"); t.Fault = null; yield break; }
            this.Ledger.Admitted(op, isNew.Value ? "TransactionMonitor.cs:77 transaction registered" : "TransactionMonitor.cs:53 joined the thread transaction",
                this.Scopes(monitor));

            yield return Step.At($"Snapshot(write {collection}): LockService.EnterLock");
            foreach (var step in monitor.Locker.EnterLock(t, collection, PragmaTimeout)) yield return step;
            if (t.Fault == null)
            {
                transaction.Value.LockedCollection = transaction.Value.LockedCollection ?? collection;
                if (callback != null)
                {
                    foreach (var step in callback()) yield return step;
                    // Insert.cs:33 validates the engine before each document the input yields.
                    if (!this.State.Valid) t.Fault = this.State.Failure ?? Faults.EngineDisposed;
                }
            }
            if (t.Fault == null && isNew.Value)
            {
                foreach (var step in this.CommitAndRelease(t, transaction.Value, injectFailure)) yield return step;
            }
            if (t.Fault == null) yield break;

            // Transaction.cs:117-127: a non-fatal error rolls an active transaction back and rethrows.
            var fault = t.Fault;
            t.Fault = null;
            if (fault != Faults.InjectedIo && transaction.Value.State == TxnState.Active)
            {
                foreach (var step in this.RollbackAndRelease(t, transaction.Value)) yield return step;
                t.Fault = null;
                if (transaction.Value.Explicit) this.Ledger.TransactionLive(op, false, "Transaction.cs:123 explicit transaction aborted");
            }
            this.Fail(op, fault, "Transaction.cs:126 rethrows");
        }

        /// <summary>LiteEngine.CommitAndReleaseTransaction (Transaction.cs:130-151).</summary>
        private IEnumerable<Step> CommitAndRelease(ModelThread t, TxnModel transaction, bool injectFailure)
        {
            yield return Step.At("Transaction.cs:134 TransactionService.Commit");
            // TransactionService.cs:290 ENSURE(Active): a transaction disposed by close cannot commit.
            var fault = transaction.State != TxnState.Active ? Faults.TransactionDisposed : injectFailure ? Faults.InjectedIo : null;
            if (fault == null)
            {
                transaction.State = TxnState.Committed;
                if (transaction.LockedCollection != null) transaction.Monitor.Locker.ExitLock(t, transaction.LockedCollection);
                yield return Step.At("Transaction.cs:135 TransactionMonitor.ReleaseTransaction");
                transaction.Monitor.ReleaseTransaction(t, transaction);
                yield break;
            }
            // Transaction.cs:137-142: completion failed, publish it as fatal and rethrow.
            foreach (var step in this.Lifecycle.Stop(t, fault)) yield return step;
            t.Fault = fault;
        }

        /// <summary>LiteEngine.RollbackAndReleaseTransaction (Transaction.cs:153-170), for an active transaction.</summary>
        private IEnumerable<Step> RollbackAndRelease(ModelThread t, TxnModel transaction)
        {
            yield return Step.At("Transaction.cs:157 TransactionService.Rollback");
            transaction.State = TxnState.Aborted;
            if (transaction.LockedCollection != null) transaction.Monitor.Locker.ExitLock(t, transaction.LockedCollection);
            yield return Step.At("Transaction.cs:158 TransactionMonitor.ReleaseTransaction");
            transaction.Monitor.ReleaseTransaction(t, transaction);
        }

        /// <summary>LiteEngine.Query (Query.cs:15-50) and QueryExecutor.ExecuteQuery (QueryExecutor.cs:70-87).</summary>
        private IEnumerable<Step> OpenReader(ModelThread t, ModelOp op, Ref<ReaderModel> reader)
        {
            yield return Step.At("Query.cs:20 LiteEngine.Query: _state.Validate()");
            if (!this.State.Valid) { this.Reject(op, this.State.Failure ?? Faults.EngineDisposed, "Query.cs:20"); yield break; }
            var state = this.State;
            var monitor = this.Monitor;
            var transaction = new Ref<TxnModel>();
            var isNew = new Ref<bool>();
            yield return Step.At("QueryExecutor.cs:73 GetTransaction(create, queryOnly: true)");
            foreach (var step in monitor.GetTransaction(t, true, PragmaTimeout, transaction, isNew)) yield return step;
            if (t.Fault != null) { this.Reject(op, t.Fault, "QueryExecutor.cs:73"); t.Fault = null; yield break; }
            this.Ledger.Admitted(op, "QueryExecutor.cs:73 reader transaction", this.Scopes(monitor));
            reader.Value = new ReaderModel { Op = op, Transaction = transaction.Value, OwnsTransaction = isNew.Value, State = state };
        }

        /// <summary>
        /// BsonDataReader.Read then Dispose (BsonDataReader.cs:111, :161 at the modeled commit), each
        /// under a continuation lease; Dispose releases the reader's transaction (QueryExecutor.cs:83).
        /// </summary>
        private IEnumerable<Step> DisposeReader(ModelThread t, ReaderModel reader)
        {
            var readFailed = false;
            foreach (var step in this.Public(t, reader.Op, () => this.ReadNext(reader, failed => readFailed = failed), continuation: true)) yield return step;
            foreach (var step in this.Public(t, reader.Op, () => this.ReleaseReader(t, reader), continuation: true)) yield return step;
            if (readFailed) this.Fail(reader.Op, reader.State.Failure ?? Faults.EngineDisposed, "BsonDataReader.cs:113");
            else this.Complete(reader.Op, "reader disposed");
        }

        private IEnumerable<Step> ReadNext(ReaderModel reader, Action<bool> failed)
        {
            yield return Step.At("BsonDataReader.cs:113 Read: _state.Validate()");
            failed(!reader.State.Valid);
        }

        private IEnumerable<Step> ReleaseReader(ModelThread t, ReaderModel reader)
        {
            yield return Step.At("BsonDataReader.cs:163 Dispose: QueryExecutor releases the reader transaction");
            if (reader.OwnsTransaction) reader.Transaction.Monitor.ReleaseTransaction(t, reader.Transaction);
        }
    }
}
