using System;
using LiteDB.Spatial.Testing.Oracles.Infrastructure;
using Xunit;

namespace LiteDB.Spatial.Testing.Oracles;

/// <summary>
/// A fact that is automatically skipped when oracle integrations are disabled.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class OracleFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OracleFactAttribute"/> class.
    /// </summary>
    /// <param name="requiresDatabase">Whether the test requires database-backed integrations (e.g. PostGIS).</param>
    /// <param name="feature">An optional feature name for the skip reason.</param>
    /// <param name="requiredOracles">Optional list of oracle keys that must be enabled.</param>
    public OracleFactAttribute(bool requiresDatabase = false, string? feature = null, params string[] requiredOracles)
    {
        requiredOracles ??= Array.Empty<string>();
        var skipReason = OracleEnvironment.GetSkipReason(requiresDatabase, feature, requiredOracles);
        if (skipReason != null)
        {
            Skip = skipReason;
        }
    }
}

/// <summary>
/// A theory that is automatically skipped when oracle integrations are disabled.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OracleTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OracleTheoryAttribute"/> class.
    /// </summary>
    /// <param name="requiresDatabase">Whether the test requires database-backed integrations.</param>
    /// <param name="feature">An optional feature name for the skip reason.</param>
    /// <param name="requiredOracles">Optional list of oracle keys that must be enabled.</param>
    public OracleTheoryAttribute(bool requiresDatabase = false, string? feature = null, params string[] requiredOracles)
    {
        requiredOracles ??= Array.Empty<string>();
        var skipReason = OracleEnvironment.GetSkipReason(requiresDatabase, feature, requiredOracles);
        if (skipReason != null)
        {
            Skip = skipReason;
        }
    }
}

/// <summary>
/// Convenience alias that mirrors <see cref="OracleFactAttribute"/> while keeping the historic "skip" naming.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OracleSkipAttribute : OracleFactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OracleSkipAttribute"/> class.
    /// </summary>
    /// <param name="requiresDatabase">Whether the test requires database-backed integrations.</param>
    /// <param name="feature">An optional feature name for the skip reason.</param>
    /// <param name="requiredOracles">Optional list of oracle keys that must be enabled.</param>
    public OracleSkipAttribute(bool requiresDatabase = false, string? feature = null, params string[] requiredOracles)
        : base(requiresDatabase, feature, requiredOracles)
    {
    }
}
