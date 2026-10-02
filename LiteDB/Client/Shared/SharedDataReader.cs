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

        private int _disposed;

        public SharedDataReader(IBsonDataReader reader, Action dispose)
        {
            _reader = reader;
            _dispose = dispose;
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
#if DEBUG || TESTING
            LiteDB.Utils.WaitGraph.Enter(this.GraphOwner);
            try
            {
#endif
            return _reader.Read();
#if DEBUG || TESTING
            }
            finally { LiteDB.Utils.WaitGraph.Exit(this.GraphOwner); }
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
