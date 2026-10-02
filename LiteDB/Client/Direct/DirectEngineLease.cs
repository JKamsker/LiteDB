using System;
using System.Collections.Generic;
using LiteDB.Engine;
using LiteDB.Vector;

namespace LiteDB.Client.Direct
{
    /// <summary>A database's independently disposable reference to its process engine.</summary>
    internal sealed class DirectEngineLease : ILiteEngine
    {
        private readonly object _gate = new object();
        private DirectEnginePool.Entry _entry;
        private readonly EngineContext _context;

        internal DirectEngineLease(DirectEnginePool.Entry entry, EngineSettings settings)
        {
            _context = new EngineContext(entry.Engine, settings.Clone());
            entry.Retain();
            _entry = entry;
        }

        internal LiteEngine Engine { get { lock (_gate) return _entry?.Engine; } }

        internal TransactionResources OpenTransactionResources()
        {
            lock (_gate)
            {
                if (_entry == null) throw new ObjectDisposedException(nameof(LiteDatabase));
                var entry = _entry;
                entry.Retain();
                _context.Retain();
                return new TransactionResources(entry.Engine, _context, () =>
                {
                    try { _context.Release(disposing: true); }
                    finally { entry.Release(disposing: true); }
                }, () =>
                {
                    try { _context.Release(disposing: false); }
                    finally { entry.Release(disposing: false); }
                });
            }
        }

        private Use Enter()
        {
            Use use;
            lock (_gate)
            {
                if (_entry == null) throw new ObjectDisposedException(nameof(LiteDatabase));
                _entry.Retain();
                _context.Retain();
                use = new Use(_entry, _context);
            }
            try { use.Engine.ReleaseAbandonedContexts(); return use; }
            catch { use.Dispose(); throw; }
        }

        private readonly struct Use : IDisposable
        {
            internal readonly DirectEnginePool.Entry Entry;
            private readonly EngineContext _context;
            private readonly EngineContext.Scope _scope;
            internal LiteEngine Engine => Entry.Engine;
            internal Use(DirectEnginePool.Entry entry, EngineContext context)
            { Entry = entry; _context = context; _scope = context.Enter(); }
            internal IBsonDataReader Transfer(IBsonDataReader reader)
            {
                var result = new DirectEngineReader(reader, Entry, _context);
                _scope.Dispose();
                return result;
            }
            public void Dispose()
            {
                _scope.Dispose();
                try { _context.Release(disposing: true); }
                finally { Entry.Release(disposing: true); }
            }
        }

        public int Checkpoint() { using var use = Enter(); return use.Engine.Checkpoint(); }
        public bool BeginTrans() { using var use = Enter(); return use.Engine.BeginTrans(); }
        public bool Commit() { using var use = Enter(); return use.Engine.Commit(); }
        public bool Rollback() { using var use = Enter(); return use.Engine.Rollback(); }
        public int Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId)
        { using var use = Enter(); return use.Engine.Insert(collection, docs, autoId); }
        public int Update(string collection, IEnumerable<BsonDocument> docs)
        { using var use = Enter(); return use.Engine.Update(collection, docs); }
        public int UpdateMany(string collection, BsonExpression transform, BsonExpression predicate)
        { using var use = Enter(); return use.Engine.UpdateMany(collection, transform, predicate); }
        public int Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId)
        { using var use = Enter(); return use.Engine.Upsert(collection, docs, autoId); }
        public int Delete(string collection, IEnumerable<BsonValue> ids)
        { using var use = Enter(); return use.Engine.Delete(collection, ids); }
        public int DeleteMany(string collection, BsonExpression predicate)
        { using var use = Enter(); return use.Engine.DeleteMany(collection, predicate); }
        public bool DropCollection(string name) { using var use = Enter(); return use.Engine.DropCollection(name); }
        public bool RenameCollection(string name, string newName)
        { using var use = Enter(); return use.Engine.RenameCollection(name, newName); }
        public bool EnsureIndex(string collection, string name, BsonExpression expression, bool unique)
        { using var use = Enter(); return use.Engine.EnsureIndex(collection, name, expression, unique); }
        public bool EnsureVectorIndex(string collection, string name, BsonExpression expression, VectorIndexOptions options)
        { using var use = Enter(); return use.Engine.EnsureVectorIndex(collection, name, expression, options); }
        public bool DropIndex(string collection, string name)
        { using var use = Enter(); return use.Engine.DropIndex(collection, name); }
        public BsonValue Pragma(string name) { using var use = Enter(); return use.Engine.Pragma(name); }
        public bool Pragma(string name, BsonValue value) { using var use = Enter(); return use.Engine.Pragma(name, value); }

        public long Rebuild(RebuildOptions options)
        {
            using var use = Enter();
            lock (use.Entry.Gate)
            {
                if (use.Entry.Rebuilding) throw new InvalidOperationException("The Direct engine is already rebuilding.");
                use.Entry.Rebuilding = true;
            }
            try { return use.Engine.Rebuild(options); }
            finally { lock (use.Entry.Gate) use.Entry.Rebuilding = false; }
        }

        public IBsonDataReader Query(string collection, Query query)
        {
            var use = Enter();
            IBsonDataReader reader = null;
            try
            {
                reader = use.Engine.Query(collection, query);
                return use.Transfer(reader);
            }
            catch
            {
                try { reader?.Dispose(); }
                finally { use.Dispose(); }
                throw;
            }
        }

        public void Dispose()
        {
            Release(disposing: true);
            GC.SuppressFinalize(this);
        }

        [LiteDB.Utils.TeardownPath("DirectEngineLease.Release", LiteDB.Utils.TeardownDisposition.Propagated,
            "The context release (rollback of its own work) and the host reference release propagate; the host reference is " +
            "released even when the context release fails (DirectEngineLease.cs try/finally).")]
        private void Release(bool disposing)
        {
            DirectEnginePool.Entry entry;
            lock (_gate) { entry = _entry; _entry = null; }
            if (entry == null) return;
            try
            {
                LiteDB.Utils.TeardownSteps.Before("DirectEngineLease.Release.context");
                _context.Release(disposing);
                LiteDB.Utils.TeardownSteps.After("DirectEngineLease.Release.context");
            }
            finally
            {
                LiteDB.Utils.TeardownSteps.Before("DirectEngineLease.Release.entry");
                entry.Release(disposing);
                LiteDB.Utils.TeardownSteps.After("DirectEngineLease.Release.entry");
            }
        }

        ~DirectEngineLease() { Release(disposing: false); }
    }
}
