using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of the SQL function registry.
    /// </summary>
    internal sealed class SqlFunctionRegistry : ISqlFunctionRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, SqlFunctionRegistration> _functions =
            new Dictionary<string, SqlFunctionRegistration>(StringComparer.OrdinalIgnoreCase);

        public void Register(SqlFunctionRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            lock (_sync)
            {
                if (_functions.ContainsKey(registration.FunctionName))
                {
                    throw new InvalidOperationException(
                        $"A SQL function named '{registration.FunctionName}' is already registered.");
                }

                _functions[registration.FunctionName] = registration;
            }
        }

        public bool TryGet(string functionName, out SqlFunctionRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(functionName))
            {
                registration = null;
                return false;
            }

            lock (_sync)
            {
                return _functions.TryGetValue(functionName, out registration);
            }
        }

        public IReadOnlyCollection<SqlFunctionRegistration> GetAll()
        {
            lock (_sync)
            {
                return _functions.Values.ToArray();
            }
        }
    }
}
