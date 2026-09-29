using System;
using LiteDB.Client.Direct;
using LiteDB.Engine;

namespace LiteDB
{
    public partial class LiteDatabase : ILiteTransactionProvider
    {
        private readonly SessionLifetime _lifetime = new SessionLifetime();

        /// <summary>
        /// Starts an independent synchronous transaction. Ordinary database collections do not
        /// enlist; use the returned handle's collections. Sequential thread handoff is supported.
        /// </summary>
        public ILiteTransaction BeginTransaction()
        {
            using var admission = _lifetime.Enter();
            TransactionResources resources;
            if (_engine is DirectEngineLease direct) resources = direct.OpenTransactionResources();
            else if (_engine is SharedEngine shared) resources = shared.OpenTransactionResources(_lifetime.Closing, _lifetime.DependencyToken);
            else if (_engine is LiteEngine engine) resources = new TransactionResources(engine, engine.CurrentContext, () => { });
            else throw new NotSupportedException("This engine does not support thread-independent transaction handles.");
            var transaction = new LiteTransaction(resources, Mapper, _lifetime);
            try { _lifetime.Register(transaction); return transaction; }
            catch { transaction.RequestClose(); throw; }
        }
    }
}
