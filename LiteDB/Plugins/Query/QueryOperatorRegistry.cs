using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB.Plugins;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of <see cref="IQueryOperatorRegistry"/>.
    /// </summary>
    public sealed class QueryOperatorRegistry : IQueryOperatorRegistry
    {
        private readonly IPluginContextFreezeState _freezeState;
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryOperatorRegistration> _operators = new Dictionary<string, QueryOperatorRegistration>(StringComparer.OrdinalIgnoreCase);

        public QueryOperatorRegistry()
            : this(null)
        {
        }

        internal QueryOperatorRegistry(IPluginContextFreezeState freezeState)
        {
            _freezeState = freezeState;
        }

        public void Register(QueryOperatorRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            _freezeState?.EnsureNotFrozen();

            lock (_sync)
            {
                if (_operators.ContainsKey(registration.OperatorName))
                {
                    throw new InvalidOperationException($"Query operator '{registration.OperatorName}' is already registered by plugin '{_operators[registration.OperatorName].PluginId}'.");
                }

                _operators[registration.OperatorName] = registration;
            }
        }

        public bool TryGet(string operatorName, out QueryOperatorRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(operatorName))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                return _operators.TryGetValue(operatorName, out registration);
            }
        }

        public IReadOnlyCollection<QueryOperatorRegistration> Registered
        {
            get
            {
                lock (_sync)
                {
                    return _operators.Values.ToArray();
                }
            }
        }
    }
}
