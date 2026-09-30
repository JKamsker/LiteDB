using System;
using System.Collections;
using System.Collections.Generic;

namespace LiteDB
{
    /// <summary>Includes deserialization and iterator cleanup in each public operation.</summary>
    internal sealed class TransactionEnumerable<T> : IEnumerable<T>
    {
        private readonly LiteTransaction _owner;
        private readonly IEnumerable<T> _source;
        internal TransactionEnumerable(LiteTransaction owner, IEnumerable<T> source) { _owner = owner; _source = source; }
        public IEnumerator<T> GetEnumerator() => _owner.Run(() => (IEnumerator<T>)new Enumerator(_owner, _source.GetEnumerator()));
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly LiteTransaction _owner;
            private IEnumerator<T> _inner;
            internal Enumerator(LiteTransaction owner, IEnumerator<T> inner) { _owner = owner; _inner = inner; }
            private IEnumerator<T> Inner => _inner ?? throw new ObjectDisposedException(nameof(IEnumerator<T>));
            public T Current { get { var inner = Inner; return _owner.Run(() => inner.Current); } }
            object IEnumerator.Current => Current;
            public bool MoveNext() { var inner = Inner; return _owner.Run(() => inner.MoveNext()); }
            public void Reset() => throw new NotSupportedException();
            public void Dispose()
            {
                if (_inner == null) return;
                _owner.DisposeBoundObject(() => _inner.Dispose());
                // Terminal/session cleanup owns every tracked engine cursor.
                _inner = null;
            }
        }
    }

    internal sealed class GuardedTransactionReader : IBsonDataReader
    {
        private readonly LiteTransaction _owner;
        private IBsonDataReader _inner;
        internal GuardedTransactionReader(LiteTransaction owner, IBsonDataReader inner) { _owner = owner; _inner = inner; }
        private IBsonDataReader Inner => _inner ?? throw new ObjectDisposedException(nameof(IBsonDataReader));
        public BsonValue Current { get { var inner = Inner; return _owner.Run(() => inner.Current); } }
        public BsonValue this[string field] { get { var inner = Inner; return _owner.Run(() => inner[field]); } }
        public string Collection { get { var inner = Inner; return _owner.Run(() => inner.Collection); } }
        public bool HasValues { get { var inner = Inner; return _owner.Run(() => inner.HasValues); } }
        public bool Read() { var inner = Inner; return _owner.Run(() => inner.Read()); }
        public void Dispose()
        {
            if (_inner == null) return;
            _owner.DisposeBoundObject(() => _inner.Dispose());
            _inner = null;
        }
    }
}
