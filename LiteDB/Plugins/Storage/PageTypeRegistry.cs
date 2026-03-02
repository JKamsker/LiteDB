using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Storage
{
    /// <summary>
    /// Thread-safe <see cref="IPageTypeRegistry"/> implementation used by <see cref="DefaultPluginContext"/>.
    /// </summary>
    public sealed class PageTypeRegistry : IPageTypeRegistry
    {
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<byte, PageFactoryRegistration> _byCode = new Dictionary<byte, PageFactoryRegistration>();
        private readonly Dictionary<string, PageFactoryRegistration> _byName = new Dictionary<string, PageFactoryRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PageFactoryRegistration> _byPluginAndName = new Dictionary<string, PageFactoryRegistration>(StringComparer.Ordinal);

        public PageTypeRegistry()
            : this(null)
        {
        }

        internal PageTypeRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState;
        }

        public void Register(PageFactoryRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            _freezeState?.EnsureNotFrozen();

            ReservedCodeRanges.EnsurePluginOwnsReservedRange(
                registration.PluginId,
                registration.NumericCode,
                ReservedCodeRanges.VectorPageStart,
                ReservedCodeRanges.VectorPageEnd,
                ReservedCodeRanges.VectorPluginId,
                "Page type code");

            lock (_sync)
            {
                if (_byCode.TryGetValue(registration.NumericCode, out var existingByCode))
                {
                    throw new InvalidOperationException($"Page type code 0x{registration.NumericCode:X2} is already registered by plugin '{existingByCode.PluginId}'.");
                }

                if (_byName.TryGetValue(registration.PageType, out var existingByName))
                {
                    throw new InvalidOperationException($"Page type '{registration.PageType}' is already registered by plugin '{existingByName.PluginId}'.");
                }

                var compositeKey = CreateCompositeKey(registration.PluginId, registration.PageType);
                if (_byPluginAndName.ContainsKey(compositeKey))
                {
                    throw new InvalidOperationException($"Plugin '{registration.PluginId}' has already registered a page type named '{registration.PageType}'.");
                }

                _byCode[registration.NumericCode] = registration;
                _byName[registration.PageType] = registration;
                _byPluginAndName[compositeKey] = registration;
            }
        }

        public bool TryGet(byte pageTypeCode, out PageFactoryRegistration registration)
        {
            lock (_sync)
            {
                return _byCode.TryGetValue(pageTypeCode, out registration);
            }
        }

        public bool TryGet(string pageTypeName, out PageFactoryRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(pageTypeName))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                return _byName.TryGetValue(pageTypeName, out registration);
            }
        }

        public bool TryGetByName(string pluginId, string pageTypeName, out PageFactoryRegistration registration)
        {
            registration = null;

            if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(pageTypeName))
            {
                return false;
            }

            var key = CreateCompositeKey(pluginId, pageTypeName);

            lock (_sync)
            {
                return _byPluginAndName.TryGetValue(key, out registration);
            }
        }

        public IReadOnlyCollection<PageFactoryRegistration> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _byCode.Values.ToArray();
                }
            }
        }

        private static string CreateCompositeKey(string pluginId, string pageTypeName)
        {
            return $"{pluginId}|{pageTypeName}";
        }
    }
}
