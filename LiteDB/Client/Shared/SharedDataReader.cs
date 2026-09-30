using LiteDB.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using LiteDB.Utils;

namespace LiteDB
{
    public class SharedDataReader : IBsonDataReader
    {
        private readonly IBsonDataReader _reader;
        private readonly Action _dispose;
        private readonly LiteEngine _ownedSnapshot;
        private readonly SharedEngine _mutexOwner;

        private int _disposed;

        public SharedDataReader(IBsonDataReader reader, Action dispose) : this(reader, dispose, null)
        {
        }

        internal SharedDataReader(IBsonDataReader reader, Action dispose, LiteEngine ownedSnapshot, SharedEngine mutexOwner = null)
        {
            _reader = reader;
            _dispose = dispose;
            _ownedSnapshot = ownedSnapshot;
            _mutexOwner = mutexOwner;
        }

        public BsonValue this[string field] => _reader[field];

        public string Collection => _reader.Collection;

        public BsonValue Current => _reader.Current;

        public bool HasValues => _reader.HasValues;

#if DEBUG || TESTING
        /// <summary>
        /// Wait-for graph: the owner whose hold this reader retains (its connection, or the pin it
        /// streams under). Its reads and disposal execute that owner's work on the calling thread.
        /// </summary>
        internal object GraphOwner { get; set; }
#endif

        public bool Read()
        {
            using var callback = new SharedEngine.CallbackScope(_mutexOwner);
#if DEBUG || TESTING
            LiteDB.Utils.WaitGraph.Enter(this.GraphOwner);
            try { return _reader.Read(); }
            finally { LiteDB.Utils.WaitGraph.Exit(this.GraphOwner); }
#else
            return _reader.Read();
#endif
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SharedDataReader()
        {
            this.Dispose(false);
        }

        [TeardownPath("SharedDataReader.Dispose", TeardownDisposition.Propagated | TeardownDisposition.Discarded,
            "The inner reader's and the release callback's failures propagate (try/finally runs the callback either way); " +
            "the core closes the release triggers (pin end, last-reader checkpoint) drop their failure lists.")]
        protected virtual void Dispose(bool disposing)
        {
            // A leased snapshot has no parent-owned fallback for a refused core close.
            // Refuse before mutating the cursor or latching disposal, so the caller can
            // retry after this snapshot's executing callback has unwound.
            if (Volatile.Read(ref _disposed) != 0) return;
            if (disposing && _ownedSnapshot?.IsExecutingOnCurrentThread == true)
                throw new InvalidOperationException("Cannot dispose a leased reader from inside its executing operation.");

            // Atomic admission: the callback ends one mutex recursion and one engine user.
            // Two threads disposing at once must not both run it, or the second would end
            // another reader's ownership and could close the engine under it.
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            if (disposing)
            {
#if DEBUG || TESTING
                LiteDB.Utils.WaitGraph.Enter(this.GraphOwner);
                try
                {
#endif
                try { _reader.Dispose(); }
                finally { _dispose(); }
#if DEBUG || TESTING
                }
                finally { LiteDB.Utils.WaitGraph.Exit(this.GraphOwner); }
#endif
            }
        }
    }
}
