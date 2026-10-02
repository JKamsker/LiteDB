using System;
using System.Collections.Generic;

namespace LiteDB.Tests.Safety
{
    // Historical adapter (PR #133 replay overlay, written from the code and docs/transaction-handles.md at
    // 39f6c6b0): sweep drivers and step catalog entries for the teardown paths that exist only on the
    // transaction-handle branch. docs/teardown-sweep.md section "OVERLAY how-to" describes the hooks.

    internal static partial class TeardownDrivers
    {
        static partial void Overlay(List<TeardownDriver> drivers) => drivers.AddRange(HandleDrivers());

        private static IEnumerable<TeardownDriver> HandleDrivers()
        {
            foreach (var mode in new[] { TeardownMode.Direct, TeardownMode.Shared })
            {
                var m = mode == TeardownMode.Direct ? "direct" : "shared";
                yield return H("LiteTransaction.ReleaseResources", m + "-commit", mode, "Commit() of a handle with pending inserts", c =>
                {
                    var tx = Handle(c, Facade(c, mode), 700);
                    c.Invoke(tx.Commit);
                    Settle(c, tx, 700);
                });
                yield return H("LiteTransaction.ReleaseResources", m + "-rollback", mode, "Rollback() of a handle with pending inserts", c =>
                {
                    var tx = Handle(c, Facade(c, mode), 710);
                    c.Invoke(tx.Rollback);
                    Settle(c, tx, 710);
                });
                yield return H("LiteTransaction.Dispose", m + "-active", mode, "Dispose() of an Active handle (rolls it back)", c =>
                {
                    var tx = Handle(c, Facade(c, mode), 720);
                    c.Invoke(tx.Dispose);
                    Settle(c, tx, 720);
                });
                yield return H("SessionLifetime.TryFinish", m + "-idle-handle", mode,
                    "LiteDatabase.Dispose() with an idle Active handle (the session close rolls it back, then releases the engine)", c =>
                {
                    var db = Facade(c, mode);
                    var tx = Handle(c, db, 730);
                    c.Invoke(db.Dispose);
                    Settle(c, tx, 730);
                });
            }
            yield return H("TransactionHolder.Run", "commit", TeardownMode.Shared,
                "Commit() of a Shared handle; its holder closes the child core and releases the writer mutex", c =>
            {
                var tx = Handle(c, Facade(c, TeardownMode.Shared), 740);
                c.Invoke(tx.Commit);
                Settle(c, tx, 740);
            });
            yield return H("TransactionHolder.Run", "dispose-active", TeardownMode.Shared,
                "Dispose() of an Active Shared handle; its holder rolls back and cleans up", c =>
            {
                var tx = Handle(c, Facade(c, TeardownMode.Shared), 750);
                c.Invoke(tx.Dispose);
                Settle(c, tx, 750);
            });
            yield return new TeardownDriver("TransactionHolder.Run", "admission-timeout", TeardownMode.Shared,
                "a bounded Shared begin whose holder times out on the writer mutex an explicit transaction of the same connection holds", c =>
            {
                var db = Facade(c, TeardownMode.Shared);
                // Last, as in the Shared drivers: the legacy owner holds the writer mutex until the scenario ends.
                TeardownStates.PendingTransaction(db, c);
                c.Invoke(() => db.BeginTransaction(TimeSpan.FromMilliseconds(300)).Dispose());
                // The documented admission timeout is the entry's own outcome (its cleanup is what is swept).
                if (c.Thrown is TimeoutException) c.Primary = c.Thrown;
            }, () => TeardownPrior.Minimal(), require: prior => prior.PendingTransaction = true);
            yield return H("DirectEngineLease.Release", "last-lease", TeardownMode.Direct,
                "disposing a facade's Direct lease (the last reference closes the pooled host)", c =>
            {
                var db = Facade(c, TeardownMode.Direct);
                var lease = ConnectionCleanProbe.EngineOf(db);
                c.Disposed.Add(lease);
                c.Invoke(lease.Dispose);
            });
            yield return H("DirectEnginePool.Entry.Release", "two-facades", TeardownMode.Direct,
                "disposing the second of two facades on one pooled Direct host (its release closes the host)", c =>
            {
                var first = Facade(c, TeardownMode.Direct);
                var second = new LiteDatabase(Connect(c, TeardownMode.Direct));
                c.Disposed.Add(second);
                c.Defer(second.Dispose);
                second.GetCollection("rows").Insert(TeardownStates.Row(760));
                c.Ledger.Acknowledge("rows", 760, TeardownStates.Row(760));
                first.Dispose();
                c.Invoke(second.Dispose);
            });
        }

        private static ConnectionString Connect(TeardownCase c, TeardownMode mode) => new ConnectionString
        {
            Filename = c.DatabasePath, Password = c.Password,
            Connection = mode == TeardownMode.Shared ? ConnectionType.Shared : ConnectionType.Direct
        };

        /// <summary>
        /// A facade with recent writes and the case's participants; disposed at scenario end. No pending
        /// legacy transaction in Shared mode: its owner would hold the writer mutex a handle begin waits for.
        /// </summary>
        private static LiteDatabase Facade(TeardownCase c, TeardownMode mode)
        {
            TeardownStates.Database(c);
            var db = new LiteDatabase(Connect(c, mode));
            c.Disposed.Add(db);
            c.Defer(db.Dispose);
            TeardownStates.RecentWrites(db, c);
            if (mode == TeardownMode.Shared && c.Prior.Peer)
            {
                var peer = new LiteDatabase(Connect(c, mode));
                peer.GetCollection("rows").Insert(TeardownStates.Row(Peer));
                c.Ledger.Acknowledge("rows", Peer, TeardownStates.Row(Peer));
                c.Defer(peer.Dispose);
            }
            if (c.Prior.OpenReader) TeardownStates.OpenReader(db, c);
            if (c.Prior.SpilledSort) TeardownStates.SpilledReader(db, c, fileScratch: mode == TeardownMode.Direct);
            if (c.Prior.PendingTransaction && mode == TeardownMode.Direct) TeardownStates.PendingTransaction(db, c);
            return db;
        }

        /// <summary>A handle (bounded Shared admission) with three pending inserts into <c>rows</c>.</summary>
        private static ILiteTransaction Handle(TeardownCase c, LiteDatabase db, int first)
        {
            var tx = db.BeginTransaction(TimeSpan.FromSeconds(20));
            c.Defer(() => { try { tx.Dispose(); } catch (Exception) { /* already completed or refused: the oracles judge the teardown */ } });
            var rows = tx.GetCollection("rows");
            for (var id = first; id < first + 3; id++) rows.Insert(TeardownStates.Row(id));
            return tx;
        }

        /// <summary>
        /// Ledger the handle's inserts by its terminal state: Committed rows must survive, RolledBack/Failed
        /// rows must be absent; an Indeterminate (or still Active) outcome is not judged.
        /// </summary>
        private static void Settle(TeardownCase c, ILiteTransaction tx, int first)
        {
            var state = tx.State;
            for (var id = first; id < first + 3; id++)
            {
                if (state == LiteTransactionState.Committed) c.Ledger.Acknowledge("rows", id, TeardownStates.Row(id));
                else if (state == LiteTransactionState.RolledBack || state == LiteTransactionState.Failed) c.Ledger.Abort("rows", id, null);
            }
        }

        private static TeardownDriver H(string path, string variant, TeardownMode mode, string entry, Action<TeardownCase> drive) =>
            new TeardownDriver(path, variant, mode, entry, drive, () => TeardownPrior.Minimal(),
                require: mode == TeardownMode.Shared ? prior => prior.PendingTransaction = false : (Action<TeardownPrior>)null);
    }

    internal static partial class TeardownStepCatalog
    {
        // A skipped release of native admission (mode guard, host, lease, session engine) also blocks the cold
        // reopen in this process, so its skip excuses durable.REOPEN_FAILED (the data check cannot run).
        private const string Reopen = "durable.REOPEN_FAILED";

        static partial void OverlaySteps(List<TeardownStepInfo> steps) => steps.AddRange(new[]
        {
            Step("LiteEngine.Close.mode-guard", "releasing the engine's native mode-admission guard", Handles, Reopen),
            Step("LiteEngine.CloseOnError.mode-guard", "releasing the engine's native mode-admission guard", Handles, Reopen),
            Step("SharedEngine.Dispose.cached-child", "disposing the idle cached transaction-handle child wrapper",
                Handles, Threads, OwnerThread, Reopen),
            Step("SharedEngine.Dispose.admission", "disposing the connection's Shared mode-admission lease", Handles, Reopen),
            Step("LiteTransaction.ReleaseResources.resources",
                "releasing a handle's storage dependency (Direct context and host reference, or the Shared holder's core and writer mutex)",
                Handles, Mutex, Threads, OwnerThread, Transactions, Readers, Reopen, "participant"),
            // Its own failures always release in RollbackCore's finally; only a fault after the rollback is realistic.
            Step("LiteTransaction.Dispose.rollback", "rolling back an Active handle on Dispose (readers, rollback, resource release)",
                TeardownModels.FailInside),
            Step("SessionLifetime.TryFinish.release", "the session's final release of its owned engine (checkpoint override, engine/lease dispose)",
                Handles, Mutex, Threads, OwnerThread, Transactions, Readers, Scratch, Reopen, "connection.engine", "connection.cores",
                "connection.ownership", "connection.pins", "connection.admissions", "connection.threads"),
            Step("TransactionHolder.Run.close-database", "closing the holder child's storage core (WAL and checkpoint I/O)", Handles),
            Step("TransactionHolder.Run.end-admissions", "ending the holder child's admitted calls"),
            Step("TransactionHolder.Run.child-admission", "releasing the child's mode admission and coordination before caching it", Handles),
            Step("TransactionHolder.Run.dispose-child", "disposing a child wrapper that is not cached", Handles, Mutex, Threads, OwnerThread, Reopen),
            Step("DirectEngineLease.Release.context", "releasing the lease's engine context (rolls back its own open transactions)", Transactions),
            Step("DirectEngineLease.Release.entry", "releasing the lease's reference on the pooled Direct host (the last one closes it)",
                Handles, Scratch, Readers, Reopen),
            Step("DirectEnginePool.Entry.Release.engine", "disposing the pooled host engine at its last release",
                Handles, Scratch, Readers, Transactions, Reopen),
        });
    }
}
