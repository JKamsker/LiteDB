using System;
using System.Collections.Generic;

namespace LiteDB.Plugins.Query
{
    /// <summary>
    /// Registry for plugin-defined SQL functions.
    /// Example: LiteDB.Vector registers VECTOR_DIST, VECTOR_SIM functions.
    /// </summary>
    public interface ISqlFunctionRegistry
    {
        /// <summary>
        /// Registers a SQL function provided by a plugin.
        /// </summary>
        /// <param name="registration">The function registration.</param>
        /// <exception cref="ArgumentNullException">When registration is null.</exception>
        /// <exception cref="InvalidOperationException">When a function with the same name is already registered.</exception>
        void Register(SqlFunctionRegistration registration);

        /// <summary>
        /// Attempts to retrieve a function registration by name.
        /// </summary>
        /// <param name="functionName">The function name (case-insensitive).</param>
        /// <param name="registration">Receives the registration if found.</param>
        /// <returns>True if the function was found, false otherwise.</returns>
        bool TryGet(string functionName, out SqlFunctionRegistration registration);

        /// <summary>
        /// Gets all registered SQL functions.
        /// </summary>
        IReadOnlyCollection<SqlFunctionRegistration> GetAll();
    }

    /// <summary>
    /// Describes a plugin-registered SQL function.
    /// </summary>
    public sealed class SqlFunctionRegistration
    {
        /// <summary>
        /// Initializes a new SQL function registration.
        /// </summary>
        /// <param name="pluginId">The identifier of the owning plugin.</param>
        /// <param name="functionName">The SQL function name (e.g., "VECTOR_DIST").</param>
        /// <param name="implementation">The function implementation delegate.</param>
        /// <param name="minParameterCount">Minimum number of parameters.</param>
        /// <param name="maxParameterCount">Maximum number of parameters.</param>
        public SqlFunctionRegistration(
            string pluginId,
            string functionName,
            Func<BsonValue[], BsonValue> implementation,
            int minParameterCount,
            int maxParameterCount)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("Plugin ID cannot be null or whitespace.", nameof(pluginId));

            if (string.IsNullOrWhiteSpace(functionName))
                throw new ArgumentException("Function name cannot be null or whitespace.", nameof(functionName));

            if (minParameterCount < 0)
                throw new ArgumentOutOfRangeException(nameof(minParameterCount), "Minimum parameter count cannot be negative.");

            if (maxParameterCount < minParameterCount)
                throw new ArgumentOutOfRangeException(nameof(maxParameterCount), "Maximum parameter count cannot be less than minimum.");

            PluginId = pluginId;
            FunctionName = functionName;
            Implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            MinParameterCount = minParameterCount;
            MaxParameterCount = maxParameterCount;
        }

        /// <summary>
        /// Gets the identifier of the plugin that registered this function.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Gets the SQL function name.
        /// </summary>
        public string FunctionName { get; }

        /// <summary>
        /// Gets the function implementation.
        /// </summary>
        public Func<BsonValue[], BsonValue> Implementation { get; }

        /// <summary>
        /// Gets the minimum number of parameters required by this function.
        /// </summary>
        public int MinParameterCount { get; }

        /// <summary>
        /// Gets the maximum number of parameters accepted by this function.
        /// </summary>
        public int MaxParameterCount { get; }
    }
}
