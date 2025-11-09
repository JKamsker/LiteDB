using System;
using System.Collections.Generic;
using LiteDB;
using LiteDB.Plugins;
using LiteDB.Plugins.Storage;

namespace LiteDB.Engine
{
    /// <summary>
    /// Coordinates page construction between core fallbacks and plugin-provided factories.
    /// </summary>
    internal sealed class PageFactoryRegistry
    {
        private readonly ILitePluginContext _pluginContext;
        private readonly IPageFactoryRegistry _pluginRegistry;
        private readonly Dictionary<PageType, FallbackFactory> _fallbackFactories;
        private readonly Dictionary<PageType, PageFactoryRegistration> _pluginFactories;

        public PageFactoryRegistry(ILitePluginContext pluginContext)
        {
            _pluginContext = pluginContext ?? throw new ArgumentNullException(nameof(pluginContext));
            _pluginRegistry = pluginContext.PageFactories ?? throw new ArgumentException("Plugin context does not expose a page factory registry.", nameof(pluginContext));

            _fallbackFactories = CreateFallbackFactories();
            _pluginFactories = LoadPluginFactories(_pluginRegistry);
        }

        public BasePage Read(PageBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var pageType = (PageType)buffer.ReadByte(BasePage.P_PAGE_TYPE);
            var pageId = buffer.ReadUInt32(BasePage.P_PAGE_ID);

            if (TryCreatePluginPage(pageType, buffer, pageId, isNew: false, out var page))
            {
                return page;
            }

            if (_fallbackFactories.TryGetValue(pageType, out var fallback))
            {
                return fallback.CreateExisting(buffer);
            }

            if (pageType == PageType.VectorIndex)
            {
                throw VectorCompatibility.PluginRequired();
            }

            return new BasePage(buffer);
        }

        public BasePage Create(PageType pageType, PageBuffer buffer, uint pageId)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (TryCreatePluginPage(pageType, buffer, pageId, isNew: true, out var page))
            {
                return page;
            }

            if (_fallbackFactories.TryGetValue(pageType, out var fallback))
            {
                return fallback.CreateNew(buffer, pageId);
            }

            if (pageType == PageType.VectorIndex)
            {
                throw VectorCompatibility.PluginRequired();
            }

            return new BasePage(buffer, pageId, pageType);
        }

        public bool TryGetRegistration(PageType pageType, out PageFactoryRegistration registration)
        {
            return _pluginFactories.TryGetValue(pageType, out registration);
        }

        private bool TryCreatePluginPage(PageType pageType, PageBuffer buffer, uint pageId, bool isNew, out BasePage page)
        {
            page = null;

            if (!_pluginFactories.TryGetValue(pageType, out var registration))
            {
                return false;
            }

            if (registration?.Factory == null)
            {
                return false;
            }

            var context = new PageConstructionContext(_pluginContext, buffer, pageType, pageId, isNew);
            var result = registration.Factory(context);

            if (result is BasePage typedPage)
            {
                page = typedPage;
                return true;
            }

            throw new InvalidOperationException($"Page factory for '{pageType}' must return a {nameof(BasePage)} instance.");
        }

        private static Dictionary<PageType, FallbackFactory> CreateFallbackFactories()
        {
            return new Dictionary<PageType, FallbackFactory>
            {
                [PageType.Empty] = new FallbackFactory(
                    buffer => new BasePage(buffer),
                    (buffer, pageId) => new BasePage(buffer, pageId, PageType.Empty)),
                [PageType.Header] = new FallbackFactory(
                    buffer => new HeaderPage(buffer),
                    (buffer, pageId) => new HeaderPage(buffer, pageId)),
                [PageType.Collection] = new FallbackFactory(
                    buffer => new CollectionPage(buffer),
                    (buffer, pageId) => new CollectionPage(buffer, pageId)),
                [PageType.Index] = new FallbackFactory(
                    buffer => new IndexPage(buffer),
                    (buffer, pageId) => new IndexPage(buffer, pageId)),
                [PageType.Data] = new FallbackFactory(
                    buffer => new DataPage(buffer),
                    (buffer, pageId) => new DataPage(buffer, pageId))
            };
        }

        private static Dictionary<PageType, PageFactoryRegistration> LoadPluginFactories(IPageFactoryRegistry registry)
        {
            var result = new Dictionary<PageType, PageFactoryRegistration>();

            if (registry == null)
            {
                return result;
            }

            foreach (var registration in registry.Registered)
            {
                if (registration == null || string.IsNullOrWhiteSpace(registration.PageType))
                {
                    continue;
                }

                if (Enum.TryParse(registration.PageType, ignoreCase: true, out PageType pageType))
                {
                    result[pageType] = registration;
                }
            }

            return result;
        }

        private sealed class FallbackFactory
        {
            private readonly Func<PageBuffer, BasePage> _existingFactory;
            private readonly Func<PageBuffer, uint, BasePage> _newFactory;

            public FallbackFactory(Func<PageBuffer, BasePage> existingFactory, Func<PageBuffer, uint, BasePage> newFactory)
            {
                _existingFactory = existingFactory ?? throw new ArgumentNullException(nameof(existingFactory));
                _newFactory = newFactory ?? throw new ArgumentNullException(nameof(newFactory));
            }

            public BasePage CreateExisting(PageBuffer buffer)
            {
                return _existingFactory(buffer);
            }

            public BasePage CreateNew(PageBuffer buffer, uint pageId)
            {
                return _newFactory(buffer, pageId);
            }
        }
    }

    /// <summary>
    /// Context passed to plugin page factories describing the requested construction.
    /// </summary>
    internal sealed class PageConstructionContext
    {
        public PageConstructionContext(ILitePluginContext pluginContext, PageBuffer buffer, PageType pageType, uint pageId, bool isNewPage)
        {
            PluginContext = pluginContext ?? throw new ArgumentNullException(nameof(pluginContext));
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            PageType = pageType;
            PageId = pageId;
            IsNewPage = isNewPage;
        }

        public ILitePluginContext PluginContext { get; }

        public PageBuffer Buffer { get; }

        public PageType PageType { get; }

        public uint PageId { get; }

        public bool IsNewPage { get; }
    }

    /// <summary>
    /// Resolves the active page factory registry with a thread-safe fallback for plugin-free scenarios.
    /// </summary>
    internal static class PageFactoryResolver
    {
        private static readonly object _sync = new object();
        private static PageFactoryRegistry _fallbackRegistry = CreateFallbackRegistry();

        internal static PageFactoryRegistry GetRegistry(ILitePluginContext context)
        {
            if (context == null)
            {
                lock (_sync)
                {
                    return _fallbackRegistry;
                }
            }

            return new PageFactoryRegistry(context);
        }

        internal static void ReplaceFallbackRegistry(ILitePluginContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (_sync)
            {
                _fallbackRegistry = new PageFactoryRegistry(context);
            }
        }

        private static PageFactoryRegistry CreateFallbackRegistry()
        {
            var context = new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);
            return new PageFactoryRegistry(context);
        }
    }
}
