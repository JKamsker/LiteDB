using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of <see cref="IQueryCostModelRegistry"/>.
    /// </summary>
    public sealed class QueryCostModelRegistry : IQueryCostModelRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryCostModelRegistration> _registrations = new Dictionary<string, QueryCostModelRegistration>(StringComparer.OrdinalIgnoreCase);

        public void Register(QueryCostModelRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            lock (_sync)
            {
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
