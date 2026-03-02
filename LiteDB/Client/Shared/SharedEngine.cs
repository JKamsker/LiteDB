using LiteDB.Engine;
using LiteDB.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using LiteDB.Client.Shared;

namespace LiteDB
{
    public class SharedEngine : ILiteEngine, IPluginHost
    {
        private readonly EngineSettings _settings;
        private readonly Mutex _mutex;
        private LiteEngine _engine;
        private bool _transactionRunning = false;
        private ILitePluginContext _plugins;
        private readonly ThreadLocal<int> _mutexDepth = new ThreadLocal<int>(() => 0);
        private int _disposeState;

        public SharedEngine(EngineSettings settings)
        {
            _settings = settings;

            var name = SharedMutexNameFactory.Create(settings.Filename, settings.SharedMutexNameStrategy);

            try
            {
                _mutex = SharedMutexFactory.Create(name);
            }
            catch (NotSupportedException ex)
            {
                if (ex is PlatformNotSupportedException)
                {
                    throw;
                }

                throw new PlatformNotSupportedException("Shared mode is not supported in platforms that do not implement named mutex.", ex);
            }
        }

        /// <summary>
        /// Open database in safe mode
        /// </summary>
        private void OpenDatabase()
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(SharedEngine));
            }

            var depth = _mutexDepth.Value;

            if (depth == 0)
            {
                try
                {
                    _mutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                }
            }

            _mutexDepth.Value = depth + 1;

            // Don't create a new engine while a transaction is running.
            if (!_transactionRunning && _engine == null)
            {
                try
                {
                    var engine = new LiteEngine(_settings);

                    try
                    {
                        if (_plugins != null)
                        {
                            ((IPluginHost)engine).SetPluginContext(_plugins);
                        }

                        _engine = engine;
                    }
                    catch
                    {
                        engine.Dispose();
                        throw;
                    }
                }
                catch
                {
                    CloseDatabase();
                    throw;
                }
            }
        }

        /// <summary>
        /// Dequeue stack and dispose database on empty stack
        /// </summary>
        private void CloseDatabase()
        {
            var depth = _mutexDepth.Value;

            if (depth <= 0)
            {
                return;
            }

            depth--;
            _mutexDepth.Value = depth;

            if (depth != 0)
            {
                return;
            }

            // Don't dispose the engine while a transaction is running.
            LiteEngine engineToDispose = null;

            if (!_transactionRunning && _engine != null)
            {
                // If no transaction pending, dispose the engine.
                engineToDispose = _engine;
                _engine = null;
            }

            try
            {
                engineToDispose?.Dispose();
            }
            finally
            {
                // Release Mutex on every call to close DB.
                _mutex.ReleaseMutex();
            }
        }

        #region Transaction Operations

        public bool BeginTrans()
        {
            OpenDatabase();

            try
            {
                if (_engine.BeginTrans())
                {
                    _transactionRunning = true;
                    return true;
                }

                // Reentrant BeginTrans() must not leak mutex depth when the engine reports an existing transaction.
                CloseDatabase();

                return false;
            }
            catch
            {
                CloseDatabase();
                throw;
            }
        }

        public bool Commit()
        {
            if (_engine == null) return false;

            try
            {
                return _engine.Commit();
            }
            finally
            {
                _transactionRunning = false;
                CloseDatabase();
            }
        }

        public bool Rollback()
        {
            if (_engine == null) return false;

            try
            {
                return _engine.Rollback();
            }
            finally
            {
                _transactionRunning = false;
                CloseDatabase();
            }
        }

        #endregion

        #region Read Operation

        public IBsonDataReader Query(string collection, Query query)
        {
            OpenDatabase();

            try
            {
                var reader = _engine.Query(collection, query);

                return new SharedDataReader(reader, () => CloseDatabase());
            }
            catch
            {
                CloseDatabase();
                throw;
            }
        }

        public BsonValue Pragma(string name)
        {
            return QueryDatabase(() => _engine.Pragma(name));
        }

        public bool Pragma(string name, BsonValue value)
        {
            return QueryDatabase(() => _engine.Pragma(name, value));
        }

        #endregion

        void IPluginHost.SetPluginContext(ILitePluginContext context)
        {
            _plugins = context;

            if (_engine != null)
            {
                ((IPluginHost)_engine).SetPluginContext(context);
            }
        }

        #region Write Operations

        public int Checkpoint()
        {
            return QueryDatabase(() => _engine.Checkpoint());
        }

        public long Rebuild(RebuildOptions options)
        {
            return QueryDatabase(() => _engine.Rebuild(options));
        }

        public int Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId)
        {
            return QueryDatabase(() => _engine.Insert(collection, docs, autoId));
        }

        public int Update(string collection, IEnumerable<BsonDocument> docs)
        {
            return QueryDatabase(() => _engine.Update(collection, docs));
        }

        public int UpdateMany(string collection, BsonExpression extend, BsonExpression predicate)
        {
            return QueryDatabase(() => _engine.UpdateMany(collection, extend, predicate));
        }

        public int Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId)
        {
            return QueryDatabase(() => _engine.Upsert(collection, docs, autoId));
        }

        public int Delete(string collection, IEnumerable<BsonValue> ids)
        {
            return QueryDatabase(() => _engine.Delete(collection, ids));
        }

        public int DeleteMany(string collection, BsonExpression predicate)
        {
            return QueryDatabase(() => _engine.DeleteMany(collection, predicate));
        }

        public bool DropCollection(string name)
        {
            return QueryDatabase(() => _engine.DropCollection(name));
        }

        public bool RenameCollection(string name, string newName)
        {
            return QueryDatabase(() => _engine.RenameCollection(name, newName));
        }

        public bool DropIndex(string collection, string name)
        {
            return QueryDatabase(() => _engine.DropIndex(collection, name));
        }

        public bool EnsureIndex(string collection, string name, BsonExpression expression, bool unique)
        {
            return QueryDatabase(() => _engine.EnsureIndex(collection, name, expression, unique));
        }

        public bool EnsureCustomIndex(string collection, string name, string strategyKind, BsonExpression expression, BsonDocument options)
        {
            return QueryDatabase(() => _engine.EnsureCustomIndex(collection, name, strategyKind, expression, options));
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SharedEngine()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            {
                return;
            }

            if (_mutexDepth.Value != 0)
            {
                Volatile.Write(ref _disposeState, 0);
                throw new InvalidOperationException("SharedEngine cannot be disposed while it is in use on the current thread.");
            }

            Exception disposeException = null;
            var acquiredMutex = false;
            var finalize = false;

            try
            {
                try
                {
                    if (_mutex.WaitOne(TimeSpan.FromMinutes(1)) == false)
                    {
                        Volatile.Write(ref _disposeState, 0);
                        throw new TimeoutException("Timed out waiting to dispose SharedEngine while the shared mutex is held by another thread or process.");
                    }

                    acquiredMutex = true;
                    finalize = true;
                }
                catch (AbandonedMutexException)
                {
                    acquiredMutex = true;
                    finalize = true;
                }

                if (_engine != null)
                {
                    try
                    {
                        _engine.Dispose();
                    }
                    catch (Exception ex)
                    {
                        disposeException = ex;
                    }
                    finally
                    {
                        _engine = null;
                    }
                }
            }
            finally
            {
                if (acquiredMutex)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }

                if (finalize)
                {
                    _mutexDepth.Dispose();
                    _mutex.Dispose();
                    Volatile.Write(ref _disposeState, 2);
                }
            }

            if (disposeException != null)
            {
                ExceptionDispatchInfo.Capture(disposeException).Throw();
            }
        }

        private T QueryDatabase<T>(Func<T> Query)
        {
            OpenDatabase();
            try
            {
                return Query();
            }
            finally
            {
                CloseDatabase();
            }
        }
    }
}
