using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins.Storage
{
    /// <summary>
    /// Thread-safe <see cref="IPageTypeRegistry"/> implementation used by <see cref="DefaultPluginContext"/>.
    /// </summary>
    public sealed class PageTypeRegistry : IPageTypeRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<byte, PageFactoryRegistration> _byCode = new Dictionary<byte, PageFactoryRegistration>();
        private readonly Dictionary<string, PageFactoryRegistration> _byName = new Dictionary<string, PageFactoryRegistration>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PageFactoryRegistration> _byPluginAndName = new Dictionary<string, PageFactoryRegistration>(StringComparer.Ordinal);

        public void Register(PageFactoryRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            lock (_sync)
            {
                _byCode[registration.NumericCode] = registration;
                _byName[registration.PageType] = registration;
                _byPluginAndName[CreateCompositeKey(registration.PluginId, registration.PageType)] = registration;
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
