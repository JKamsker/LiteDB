using LiteDB.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;

namespace LiteDB
{
    public class SharedDataReader : IBsonDataReader
    {
        private readonly IBsonDataReader _reader;
        private readonly Action _dispose;

        private bool _disposed = false;

        public SharedDataReader(IBsonDataReader reader, Action dispose)
        {
            _reader = reader;
            _dispose = dispose;
        }

        public BsonValue this[string field] => _reader[field];

        public string Collection => _reader.Collection;

        public BsonValue Current => _reader.Current;

        public bool HasValues => _reader.HasValues;

        public bool Read() => _reader.Read();

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SharedDataReader()
        {
            this.Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            _disposed = true;

            if (disposing)
            {
                Exception disposeException = null;

                try
                {
                    _reader.Dispose();
                }
                catch (Exception ex)
                {
                    disposeException = ex;
                }
                finally
                {
                    try
                    {
                        _dispose?.Invoke();
                    }
                    catch
                    {
                        if (disposeException == null)
                        {
                            throw;
                        }
                    }
                }

                if (disposeException != null)
                {
                    ExceptionDispatchInfo.Capture(disposeException).Throw();
                }
            }
        }
    }
}
