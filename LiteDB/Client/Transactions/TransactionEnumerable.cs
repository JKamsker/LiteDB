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
            public T Current => _owner.Run(() => _inner.Current);
            object IEnumerator.Current => Current;
            public bool MoveNext() => _owner.Run(() => _inner.MoveNext());
            public void Reset() => throw new NotSupportedException();
            public void Dispose()
            {
                if (_inner == null) return;
                // Terminal cleanup already closed the underlying tracked engine cursor.
                if (_owner.State != LiteTransactionState.Active) { _inner = null; return; }
                _owner.Run(() => { _inner.Dispose(); _inner = null; return true; });
            }
        }
    }

    internal sealed class GuardedTransactionReader : IBsonDataReader
    {
        private readonly LiteTransaction _owner;
        private IBsonDataReader _inner;
        internal GuardedTransactionReader(LiteTransaction owner, IBsonDataReader inner) { _owner = owner; _inner = inner; }
        public BsonValue Current => _owner.Run(() => _inner.Current);
        public BsonValue this[string field] => _owner.Run(() => _inner[field]);
        public string Collection => _owner.Run(() => _inner.Collection);
        public bool HasValues => _owner.Run(() => _inner.HasValues);
        public bool Read() => _owner.Run(() => _inner.Read());
        public void Dispose()
        {
            if (_inner == null) return;
            if (_owner.State != LiteTransactionState.Active) { _inner = null; return; }
            _owner.Run(() => { _inner.Dispose(); _inner = null; return true; });
        }
    }
}
