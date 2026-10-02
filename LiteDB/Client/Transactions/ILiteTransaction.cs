using System;
using System.Collections.Generic;
using System.Threading;

namespace LiteDB
{
    /// <summary>The outcome of one explicitly owned transaction, independent of resource cleanup.</summary>
    public enum LiteTransactionState
    {
        Active,
        Committed,
        RolledBack,
        Failed,
        Indeterminate
    }

    /// <summary>
    /// A synchronous transaction that supports sequential thread handoff. Concurrent use is rejected.
    /// Collections obtained from this handle never fall back to automatic transactions.
    /// </summary>
    public interface ILiteTransaction : IDisposable
    {
        LiteTransactionState State { get; }
        ILiteCollection<T> GetCollection<T>(string name = null, BsonAutoId autoId = BsonAutoId.ObjectId);
        ILiteCollection<BsonDocument> GetCollection(string name, BsonAutoId autoId = BsonAutoId.ObjectId);
        IEnumerable<string> GetCollectionNames();
        bool CollectionExists(string name);
        bool DropCollection(string name);
        bool RenameCollection(string name, string newName);
        void Commit();
        void Rollback();
    }

    /// <summary>Optional capability; existing ILiteDatabase implementations need not implement it.</summary>
    public interface ILiteTransactionProvider
    {
        ILiteTransaction BeginTransaction();
    }

    /// <summary>Optional begin-time admission controls; existing providers need no new members.</summary>
    public interface ILiteTransactionAdmissionProvider : ILiteTransactionProvider
    {
        /// <summary>
        /// Bounds Shared local/native writer waits. Cancellation applies only until begin returns;
        /// engine I/O, cleanup and existing Direct/collection lock waits retain their own contracts.
        /// </summary>
        ILiteTransaction BeginTransaction(TimeSpan sharedAdmissionTimeout, CancellationToken cancellationToken = default);
    }

    public static class LiteTransactionExtensions
    {
        /// <summary>Begins with opt-in Shared writer admission controls, without changing commit cancellation.</summary>
        public static ILiteTransaction BeginTransaction(this ILiteDatabase database, TimeSpan sharedAdmissionTimeout,
            CancellationToken cancellationToken = default)
        {
            LiteDB.Utils.Reachability.Sometimes("api:LiteTransactionExtensions.BeginTransaction");
            if (database == null) throw new ArgumentNullException(nameof(database));
            TransactionAdmission.Validate(sharedAdmissionTimeout);
            cancellationToken.ThrowIfCancellationRequested();
            if (database is ILiteTransactionAdmissionProvider provider)
                return provider.BeginTransaction(sharedAdmissionTimeout, cancellationToken);
            LiteDB.Utils.Reachability.Sometimes("refusal:handle-admission-provider-unsupported");
            throw new NotSupportedException("This database provider does not support transaction admission controls.");
        }

        /// <summary>Creates an independent handle without enlisting ordinary database collections.</summary>
        public static ILiteTransaction BeginTransaction(this ILiteDatabase database)
        {
            LiteDB.Utils.Reachability.Sometimes("api:LiteTransactionExtensions.BeginTransaction");
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (database is ILiteTransactionProvider provider) return provider.BeginTransaction();
            LiteDB.Utils.Reachability.Sometimes("refusal:handle-unsupported-engine");
            throw new NotSupportedException("This database provider does not support thread-independent transaction handles.");
        }
    }
}
