using System;

namespace LiteDB.Client.Direct
{
    /// <summary>Transfers the query's engine reference to its returned reader.</summary>
    internal sealed class DirectEngineReader : IBsonDataReader
    {
        private readonly object _gate = new object();
        private IBsonDataReader _reader;
        private DirectEnginePool.Entry _entry;
        private bool _reading, _disposed;

        internal DirectEngineReader(IBsonDataReader reader, DirectEnginePool.Entry entry)
        { _reader = reader; _entry = entry; }

        public BsonValue this[string field] { get { lock (_gate) { Check(); return _reader[field]; } } }
        public string Collection { get { lock (_gate) { Check(); return _reader.Collection; } } }
        public BsonValue Current { get { lock (_gate) { Check(); return _reader.Current; } } }
        public bool HasValues { get { lock (_gate) { Check(); return _reader.HasValues; } } }

        public bool Read()
        {
            lock (_gate)
            {
                Check();
                if (_reading) throw new InvalidOperationException("Recursive reader iteration is unsupported.");
                _reading = true;
            }
            // User transforms may wait for disposal on another thread. Disposal
            // marks the reader closed but leaves cleanup to this active call.
            try { return _reader.Read(); }
            finally
            {
                lock (_gate)
                {
                    _reading = false;
                    if (_disposed) Close();
                }
            }
        }

        private void Check()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(IBsonDataReader));
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (!_reading) Close();
            }
            GC.SuppressFinalize(this);
        }

        private void Close()
        {
            var entry = _entry;
            var reader = _reader;
            _entry = null;
            _reader = null;
            try { reader?.Dispose(); }
            finally { entry?.Release(disposing: true); }
        }

        ~DirectEngineReader()
        {
            // The engine graph and its storage finalizers own abandoned cleanup.
            _entry?.Release(disposing: false);
        }
    }
}
