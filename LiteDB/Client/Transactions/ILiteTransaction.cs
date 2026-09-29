using System;
using System.Collections.Generic;

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

    public static class LiteTransactionExtensions
    {
        /// <summary>Creates an independent handle without enlisting ordinary database collections.</summary>
        public static ILiteTransaction BeginTransaction(this ILiteDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (database is ILiteTransactionProvider provider) return provider.BeginTransaction();
            throw new NotSupportedException("This database provider does not support thread-independent transaction handles.");
        }
    }
}
