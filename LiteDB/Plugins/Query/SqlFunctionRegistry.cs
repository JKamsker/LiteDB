using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Thread-safe implementation of <see cref="ISqlFunctionRegistry"/>.
    /// </summary>
    public sealed class SqlFunctionRegistry : ISqlFunctionRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, SqlFunctionRegistration> _registrations = new Dictionary<string, SqlFunctionRegistration>(StringComparer.OrdinalIgnoreCase);

        public void Register(SqlFunctionRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            lock (_sync)
            {
                if (_registrations.ContainsKey(registration.FunctionName))
                {
                    throw new InvalidOperationException($"SQL function '{registration.FunctionName}' is already registered by plugin '{_registrations[registration.FunctionName].PluginId}'.");
                }

                _registrations[registration.FunctionName] = registration;
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
                return _registrations.TryGetValue(functionName, out registration);
            }
        }

        public IReadOnlyCollection<SqlFunctionRegistration> Registered
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
