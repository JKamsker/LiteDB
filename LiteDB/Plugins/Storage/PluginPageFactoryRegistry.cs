using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Storage
{
    /// <summary>
    /// Thread-safe <see cref="IPageFactoryRegistry"/> implementation used by <see cref="DefaultPluginContext"/> so each database maintains isolated factory registrations.
    /// </summary>
    public sealed class PluginPageFactoryRegistry : IPageFactoryRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, PageFactoryRegistration> _factories = new Dictionary<string, PageFactoryRegistration>(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public void Register(PageFactoryRegistration registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
                _factories[registration.PageType] = registration;
            }
        }

        /// <inheritdoc />
        public bool TryGet(string pageType, out PageFactoryRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(pageType))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                return _factories.TryGetValue(pageType, out registration);
            }
        }

        /// <inheritdoc />
        public IReadOnlyCollection<PageFactoryRegistration> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _factories.Values.ToArray();
                }
            }
        }
    }
}
