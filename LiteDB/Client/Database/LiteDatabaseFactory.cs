using System;
using System.Collections.Generic;
using System.Threading;
using LiteDB.Engine;
using LiteDB.Plugins;

namespace LiteDB
{
    public sealed class LiteDatabaseFactory : ILiteDatabaseFactory
    {
        private sealed class Lease : IDisposable
        {
            private readonly LiteDatabaseFactory _factory;
            private int _released;

            public Lease(LiteDatabaseFactory factory)
            {
                _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _released, 1, 0) != 0)
                {
                    return;
                }

                _factory.ReleaseLease();
            }
        }

        private readonly ILiteEngine _engine;
        private readonly bool _ownsEngine;
        private readonly BsonMapper _mapper;
        private readonly DefaultPluginContext _pluginContext;
        private readonly IReadOnlyList<ILitePlugin> _plugins;
        private readonly IDisposable _ownedResources;

        private int _refCount;
        private int _disposed;

        internal LiteDatabaseFactory(
            ILiteEngine engine,
            bool ownsEngine,
            BsonMapper mapper,
            DefaultPluginContext pluginContext,
            IReadOnlyList<ILitePlugin> plugins,
            IDisposable ownedResources)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _ownsEngine = ownsEngine;
            _mapper = mapper ?? BsonMapper.Global;
            _pluginContext = pluginContext ?? throw new ArgumentNullException(nameof(pluginContext));
            _plugins = plugins ?? Array.Empty<ILitePlugin>();
            _ownedResources = ownedResources;
            _refCount = 1;

            try
            {
                using var host = new LiteDatabase(
                    _engine,
                    disposeOnClose: false,
                    mapper: _mapper,
                    pluginContext: _pluginContext,
                    initializePlugins: true,
                    plugins: _plugins);
            }
            catch
            {
                if (_ownsEngine)
                {
                    try
                    {
                        _engine.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }

                try
                {
                    _ownedResources?.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }

                throw;
            }
        }

        public ILiteDatabase CreateDatabase()
        {
            Interlocked.Increment(ref _refCount);

            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Decrement(ref _refCount);
                throw new ObjectDisposedException(nameof(LiteDatabaseFactory));
            }

            return new LiteDatabase(
                _engine,
                disposeOnClose: false,
                mapper: _mapper,
                pluginContext: _pluginContext,
                initializePlugins: false,
                plugins: null,
                engineLease: new Lease(this));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            ReleaseLease();
        }

        private void ReleaseLease()
        {
            if (Interlocked.Decrement(ref _refCount) != 0)
            {
                return;
            }

            if (_ownsEngine)
            {
                try
                {
                    _engine.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            try
            {
                _ownedResources?.Dispose();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}

