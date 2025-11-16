using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of <see cref="IQueryOperatorRegistry"/>.
    /// </summary>
    public sealed class QueryOperatorRegistry : IQueryOperatorRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryOperatorRegistration> _operators = new Dictionary<string, QueryOperatorRegistration>(StringComparer.OrdinalIgnoreCase);

        public void Register(QueryOperatorRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            lock (_sync)
            {
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
