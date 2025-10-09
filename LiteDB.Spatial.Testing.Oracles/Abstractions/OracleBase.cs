using LiteDB.Spatial.Testing.Oracles;

namespace LiteDB.Spatial.Testing.Oracles.Abstractions;

public abstract class OracleBase : IOracleMetadata
{
    protected OracleBase(string name, bool isDatabaseOracle)
    {
        Name = name;
        if (!OracleEnvironment.IsEnabled(name, isDatabaseOracle, out var reason))
        {
            IsAvailable = false;
            SkipReason = reason;
        }
        else
        {
            IsAvailable = true;
        }
    }

    public string Name { get; }

    public bool IsAvailable { get; protected set; }

    public string? SkipReason { get; protected set; }
}
