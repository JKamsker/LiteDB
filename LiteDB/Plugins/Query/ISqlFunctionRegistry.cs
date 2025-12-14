using System;
using System.Collections.Generic;
using LiteDB;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Registry contract for plugin-defined SQL functions.
    /// </summary>
    public interface ISqlFunctionRegistry
    {
        void Register(SqlFunctionRegistration registration);

        bool TryGet(string functionName, out SqlFunctionRegistration registration);

        IReadOnlyCollection<SqlFunctionRegistration> Registered { get; }
    }

    /// <summary>
    /// Describes a SQL function contributed by a plugin.
    /// </summary>
    public sealed class SqlFunctionRegistration
    {
        public SqlFunctionRegistration(
            string pluginId,
            string functionName,
            Func<BsonValue[], BsonValue> implementation,
            int minParameterCount,
            int maxParameterCount)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException("Plugin identifier must be provided.", nameof(pluginId));
            }

            if (string.IsNullOrWhiteSpace(functionName))
            {
                throw new ArgumentException("Function name must be provided.", nameof(functionName));
            }

            if (implementation == null)
            {
                throw new ArgumentNullException(nameof(implementation));
            }

            if (minParameterCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minParameterCount), "Minimum parameter count must be non-negative.");
            }

            if (maxParameterCount != -1 && maxParameterCount < minParameterCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maxParameterCount), "Maximum parameter count must be -1 (variadic) or greater than or equal to the minimum.");
            }

            PluginId = pluginId;
            FunctionName = functionName;
            Implementation = implementation;
            MinParameterCount = minParameterCount;
            MaxParameterCount = maxParameterCount;
        }

        public string PluginId { get; }

        public string FunctionName { get; }

        public Func<BsonValue[], BsonValue> Implementation { get; }

        public int MinParameterCount { get; }

        public int MaxParameterCount { get; }
    }
}
