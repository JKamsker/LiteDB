using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of the query cost model registry.
    /// </summary>
    internal sealed class QueryCostModelRegistry : IQueryCostModelRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryCostModelRegistration> _costModels =
            new Dictionary<string, QueryCostModelRegistration>(StringComparer.Ordinal);

        public void Register(QueryCostModelRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
                if (_costModels.ContainsKey(registration.IndexKind))
                {
                    throw new InvalidOperationException(
                        $"A cost model for index kind '{registration.IndexKind}' is already registered.");
                }

                _costModels[registration.IndexKind] = registration;
            }
        }

        public bool TryGet(string indexKind, out QueryCostModelRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(indexKind))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                return _costModels.TryGetValue(indexKind, out registration);
            }
        }

        public IEnumerable<QueryCostModelRegistration> GetAll()
        {
            lock (_sync)
            {
                return _costModels.Values.ToArray();
            }
        }
    }
}
