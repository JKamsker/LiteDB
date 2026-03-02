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
        private readonly int _ownerThreadId;

        private bool _disposed = false;

        public SharedDataReader(IBsonDataReader reader, Action dispose)
        {
            _reader = reader;
            _dispose = dispose;
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        public BsonValue this[string field]
        {
            get
            {
                EnsureOwnerThread();
                return _reader[field];
            }
        }

        public string Collection
        {
            get
            {
                EnsureOwnerThread();
                return _reader.Collection;
            }
        }

        public BsonValue Current
        {
            get
            {
                EnsureOwnerThread();
                return _reader.Current;
            }
        }

        public bool HasValues
        {
            get
            {
                EnsureOwnerThread();
                return _reader.HasValues;
            }
        }

        public bool Read()
        {
            EnsureOwnerThread();
            return _reader.Read();
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

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                EnsureOwnerThread();
                _disposed = true;

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

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException("Shared data readers must be used and disposed on the same thread they were created on.");
            }
        }
    }
}
