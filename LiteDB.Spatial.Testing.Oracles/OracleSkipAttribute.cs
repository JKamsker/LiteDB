using System;
using System.Linq;
using Xunit;

namespace LiteDB.Spatial.Testing.Oracles;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OracleSkipAttribute : FactAttribute
{
    public OracleSkipAttribute(params string[] oracleNames)
    {
        if (oracleNames is null || oracleNames.Length == 0)
        {
            return;
        }

        var disabled = oracleNames
            .Select(name => (name, allowed: OracleEnvironment.IsEnabled(name, OracleRegistry.IsDatabaseOracle(name), out var reason), reason))
            .Where(tuple => !tuple.allowed)
            .ToArray();

        if (disabled.Length > 0)
        {
            Skip = string.Join(Environment.NewLine, disabled.Select(item => $"Skipping because oracle '{item.name}' is disabled: {item.reason}"));
        }
    }
}
