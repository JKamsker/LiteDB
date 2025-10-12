using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit.Sdk;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// Skips an xUnit test when the required spatial oracle is disabled.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class OracleSkipAttribute : BeforeAfterTestAttribute
{
    private readonly string[] _requiredOracles;
    private readonly bool _requiresDatabase;

    public OracleSkipAttribute(params string[] requiredOracles)
        : this(false, requiredOracles)
    {
    }

    public OracleSkipAttribute(bool requiresDatabase, params string[] requiredOracles)
    {
        _requiresDatabase = requiresDatabase;
        _requiredOracles = requiredOracles?
            .Select(o => o?.Trim().ToLowerInvariant())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o!)
            .Distinct()
            .ToArray() ?? Array.Empty<string>();
    }

    public override void Before(MethodInfo methodUnderTest)
    {
        if (TryGetSkipReason(out var reason))
        {
            throw CreateSkipException(reason);
        }
    }

    private bool TryGetSkipReason(out string reason)
    {
        if (_requiresDatabase && !OracleEnvironment.DatabaseEnabled)
        {
            reason = "SPATIAL_DB_TESTS is not enabled.";
            return true;
        }

        foreach (var oracle in _requiredOracles)
        {
            if (!OracleEnvironment.Allows(oracle))
            {
                reason = $"Spatial oracle '{oracle}' is disabled via SPATIAL_ORACLES.";
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    private static Exception CreateSkipException(string reason)
    {
        var skipType = typeof(SkipException);
        var exception = (Exception?)Activator.CreateInstance(
            skipType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object?[] { reason },
            culture: CultureInfo.InvariantCulture);

        return exception ?? new InvalidOperationException(reason);
    }
}
