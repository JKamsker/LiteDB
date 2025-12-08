using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private readonly IPageTypeRegistry _pluginRegistry;
        private readonly Dictionary<PageType, FallbackFactory> _fallbackFactories;
        private readonly Dictionary<byte, PageFactoryRegistration> _pluginFactories;
        private readonly Dictionary<string, PageFactoryRegistration> _pluginFactoriesByName;

        public PageFactoryRegistry(ILitePluginContext pluginContext)
        {
            if (pluginContext == null)
            {
                throw new ArgumentNullException(nameof(pluginContext));
            }

            _pluginRegistry = pluginContext.PageFactories ?? throw new ArgumentException("Plugin context does not expose a page factory registry.", nameof(pluginContext));

            _fallbackFactories = CreateFallbackFactories();
            _pluginFactoriesByName = new Dictionary<string, PageFactoryRegistration>(StringComparer.OrdinalIgnoreCase);
            _pluginFactories = LoadPluginFactories(_pluginRegistry, _pluginFactoriesByName);
        }

        public BasePage Read(PageBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var pageTypeCode = buffer.ReadByte(BasePage.P_PAGE_TYPE);
            var pageType = (PageType)pageTypeCode;
            var pageId = buffer.ReadUInt32(BasePage.P_PAGE_ID);

            if (TryCreatePluginPage(pageTypeCode, buffer, pageId, isNew: false, out var page))
            {
                return page;
            }

            if (_fallbackFactories.TryGetValue(pageType, out var fallback))
            {
                return fallback.CreateExisting(buffer);
            }

            throw PluginExceptionHelper.PluginRequired(
                pluginId: null,
                message: $"Page type 0x{pageTypeCode:X2} requires a registered plugin. Install the appropriate plugin and retry.");
        }

        public BasePage Create(PageType pageType, PageBuffer buffer, uint pageId)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var pageTypeCode = (byte)pageType;

            if (TryCreatePluginPage(pageTypeCode, buffer, pageId, isNew: true, out var page))
            {
                return page;
            }

            if (_fallbackFactories.TryGetValue(pageType, out var fallback))
            {
                return fallback.CreateNew(buffer, pageId);
            }

            throw PluginExceptionHelper.PluginRequired(
                pluginId: null,
                message: $"Page type 0x{pageTypeCode:X2} requires a registered plugin. Install the appropriate plugin and retry.");
        }

        public bool TryGetRegistration(PageType pageType, out PageFactoryRegistration registration)
        {
            return _pluginFactories.TryGetValue((byte)pageType, out registration);
        }

        private bool TryCreatePluginPage(byte pageTypeCode, PageBuffer buffer, uint pageId, bool isNew, out BasePage page)
        {
            page = null;

            if (!_pluginFactories.TryGetValue(pageTypeCode, out var registration))
            {
                return false;
            }

            if (registration?.Factory == null)
            {
                return false;
            }

            var context = new PageConstructionContext(buffer, pageId, isNew);
            var result = registration.Factory(context);

            if (result is BasePage typedPage)
            {
                page = typedPage;
                return true;
            }

            throw new InvalidOperationException($"Page factory for '{pageTypeCode}' must return a {nameof(BasePage)} instance.");
        }

        public bool TryCreatePluginPage(Type requestedType, PageBuffer buffer, uint pageId, bool isNew, out BasePage page)
        {
            page = null;

            if (requestedType == null)
            {
                return false;
            }

            var typeName = requestedType.Name;

            if (string.IsNullOrWhiteSpace(typeName) || !typeName.EndsWith("Page", StringComparison.Ordinal))
            {
                return false;
            }

            var logicalName = typeName.Substring(0, typeName.Length - 4);

            if (!_pluginFactoriesByName.TryGetValue(logicalName, out var registration) || registration?.Factory == null)
            {
                return false;
            }

            var context = new PageConstructionContext(buffer, pageId, isNew);
            var result = registration.Factory(context);

            if (result is BasePage typedPage)
            {
                page = typedPage;
                return true;
            }

            throw new InvalidOperationException($"Page factory for '{logicalName}' must return a {nameof(BasePage)} instance.");
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

        private static Dictionary<byte, PageFactoryRegistration> LoadPluginFactories(IPageTypeRegistry registry, Dictionary<string, PageFactoryRegistration> factoriesByName)
        {
            var result = new Dictionary<byte, PageFactoryRegistration>();

            if (registry == null)
            {
                return result;
            }

            foreach (var registration in registry.Registered)
            {
                if (registration == null)
                {
                    continue;
                }

                result[registration.NumericCode] = registration;
                if (!string.IsNullOrWhiteSpace(registration.PageType))
                {
                    factoriesByName[registration.PageType] = registration;
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
    /// Resolves the active page factory registry with a thread-safe fallback for plugin-free scenarios.
    /// </summary>
    internal static class PageFactoryResolver
    {
        private static readonly ConditionalWeakTable<ILitePluginContext, PageFactoryRegistry> _registries = new ConditionalWeakTable<ILitePluginContext, PageFactoryRegistry>();
        private static readonly ILitePluginContext _defaultContext = CreateDefaultContext();

        internal static PageFactoryRegistry GetRegistry(ILitePluginContext context)
        {
            var target = context ?? _defaultContext;
            return _registries.GetValue(target, static ctx => new PageFactoryRegistry(ctx));
        }

        [Obsolete("Global page factory registries have been removed; registries are scoped per ILitePluginContext.")]
        internal static void ReplaceFallbackRegistry(ILitePluginContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _registries.Remove(context);
            _registries.GetValue(context, static ctx => new PageFactoryRegistry(ctx));
        }

        private static ILitePluginContext CreateDefaultContext()
        {
            return new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);
        }
    }
}
