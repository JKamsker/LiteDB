using System;
using System.Threading;
using LiteDB.Client.Direct;
using LiteDB.Engine;

namespace LiteDB
{
    public partial class LiteDatabase : ILiteTransactionAdmissionProvider
    {
        private readonly SessionLifetime _lifetime = new SessionLifetime();

        /// <summary>
        /// Starts an independent synchronous transaction. Ordinary database collections do not
        /// enlist; use the returned handle's collections. Sequential thread handoff is supported.
        /// </summary>
        public ILiteTransaction BeginTransaction()
        {
            LiteDB.Utils.Reachability.Sometimes("api:ILiteTransactionProvider.BeginTransaction");
            return BeginTransaction(System.Threading.Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Bounds the combined Shared local/native writer wait. Zero attempts immediate admission.
        /// Cancellation affects begin only; returned handles and executing commits are independent.
        /// This does not bound engine I/O, cleanup, or existing Direct/collection lock waits.
        /// </summary>
        public ILiteTransaction BeginTransaction(TimeSpan sharedAdmissionTimeout, CancellationToken cancellationToken = default)
        {
            LiteDB.Utils.Reachability.Sometimes("api:ILiteTransactionAdmissionProvider.BeginTransaction");
            TransactionAdmission.Validate(sharedAdmissionTimeout);
            cancellationToken.ThrowIfCancellationRequested();
            using var admission = _lifetime.Enter();
            TransactionResources resources;
            if (_engine is DirectEngineLease direct) resources = direct.OpenTransactionResources();
            else if (_engine is SharedEngine shared)
            {
                using var waiting = new TransactionAdmission(sharedAdmissionTimeout, cancellationToken, _lifetime.Closing);
                resources = shared.OpenTransactionResources(waiting, _lifetime.DependencyToken);
            }
            else if (_engine is LiteEngine engine) resources = new TransactionResources(engine, engine.CurrentContext, () => { });
            else
            {
                LiteDB.Utils.Reachability.Sometimes("refusal:handle-unsupported-engine");
                throw new NotSupportedException("This engine does not support thread-independent transaction handles.");
            }
            try { cancellationToken.ThrowIfCancellationRequested(); }
            catch (Exception error)
            {
                try { resources.Dispose(); }
                catch (Exception cleanup) { error.Data["LiteDB.TransactionOpenCleanup"] = cleanup; }
                throw;
            }
            var transaction = new LiteTransaction(resources, Mapper, _lifetime);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _lifetime.Register(transaction);
                return transaction;
            }
            catch (Exception error) { transaction.RequestClose(error); throw; }
        }
    }
}
