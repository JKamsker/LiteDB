using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of <see cref="IQueryCostModelRegistry"/>.
    /// </summary>
    public sealed class QueryCostModelRegistry : IQueryCostModelRegistry
    {
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryCostModelRegistration> _registrations = new Dictionary<string, QueryCostModelRegistration>(StringComparer.OrdinalIgnoreCase);

        public QueryCostModelRegistry()
            : this(null)
        {
        }

        internal QueryCostModelRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState;
        }

        public void Register(QueryCostModelRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            _freezeState?.EnsureNotFrozen();

            lock (_sync)
            {
                if (_registrations.ContainsKey(registration.IndexKind))
                {
                    throw new InvalidOperationException($"Cost model for index kind '{registration.IndexKind}' is already registered by plugin '{_registrations[registration.IndexKind].PluginId}'.");
                }

                _registrations[registration.IndexKind] = registration;
            }
        }

        public IReadOnlyCollection<QueryCostModelRegistration> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _registrations.Values.ToArray();
                }
            }
        }
    }
}
