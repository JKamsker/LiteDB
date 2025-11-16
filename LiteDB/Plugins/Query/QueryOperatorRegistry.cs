using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of the query operator registry.
    /// </summary>
    internal sealed class QueryOperatorRegistry : IQueryOperatorRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, QueryOperatorRegistration> _operators =
            new Dictionary<string, QueryOperatorRegistration>(StringComparer.OrdinalIgnoreCase);

        public void Register(QueryOperatorRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
                if (_operators.ContainsKey(registration.OperatorName))
                {
                    throw new InvalidOperationException(
                        $"A query operator named '{registration.OperatorName}' is already registered.");
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

        public IReadOnlyCollection<QueryOperatorRegistration> GetAll()
        {
            lock (_sync)
            {
                return _operators.Values.ToArray();
            }
        }
    }
}
