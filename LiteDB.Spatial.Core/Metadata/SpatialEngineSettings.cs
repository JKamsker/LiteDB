#nullable enable

using System;

namespace LiteDB.Spatial;

/// <summary>
/// Represents optional engine-specific metadata persisted with a spatial descriptor.
/// </summary>
public sealed class SpatialEngineSettings : IEquatable<SpatialEngineSettings>
{
    private SpatialEngineSettings(BoundingBox? domain, GeographicDistanceMode? distanceMode)
    {
        Domain = domain;
        DistanceMode = distanceMode;
    }

    /// <summary>
    /// Gets an instance with no additional engine metadata.
    /// </summary>
    public static SpatialEngineSettings Empty { get; } = new SpatialEngineSettings(null, null);

    /// <summary>
    /// Gets the coordinate domain associated with the engine when applicable.
    /// </summary>
    public BoundingBox? Domain { get; }

    /// <summary>
    /// Gets the distance mode configured for geographic engines.
    /// </summary>
    public GeographicDistanceMode? DistanceMode { get; }

    /// <summary>
    /// Gets a value indicating whether no settings have been configured.
    /// </summary>
    public bool IsEmpty => Domain is null && DistanceMode is null;

    /// <summary>
    /// Creates a set of settings for Cartesian engines that require a domain.
    /// </summary>
    public static SpatialEngineSettings ForCartesian(BoundingBox domain)
    {
        if (domain.Dimensions is < 2 or > 3)
        {
            throw new ArgumentException("Cartesian domains must describe either two or three dimensions.", nameof(domain));
        }

        return new SpatialEngineSettings(domain, null);
    }

    /// <summary>
    /// Creates settings describing the distance mode for geographic engines.
    /// </summary>
    public static SpatialEngineSettings ForGeographic(GeographicDistanceMode mode)
    {
        return new SpatialEngineSettings(null, mode);
    }

    /// <summary>
    /// Creates settings using the specified optional values.
    /// </summary>
    public static SpatialEngineSettings Create(BoundingBox? domain, GeographicDistanceMode? distanceMode)
    {
        if (domain is null && distanceMode is null)
        {
            return Empty;
        }

        if (domain is not null && domain.Value.Dimensions is < 2 or > 3)
        {
            throw new ArgumentException("Domains must describe two or three dimensions.", nameof(domain));
        }

        return new SpatialEngineSettings(domain, distanceMode);
    }

    /// <inheritdoc />
    public bool Equals(SpatialEngineSettings? other)
    {
        if (other is null)
        {
            return false;
        }

        var domainEqual = Nullable.Equals(Domain, other.Domain);
        var modeEqual = DistanceMode.HasValue
            ? other.DistanceMode.HasValue && DistanceMode.Value == other.DistanceMode.Value
            : !other.DistanceMode.HasValue;
        return domainEqual && modeEqual;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SpatialEngineSettings settings && Equals(settings);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 397) ^ (Domain?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (DistanceMode?.GetHashCode() ?? 0);
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Domain={Domain?.ToString() ?? "<none>"}, DistanceMode={DistanceMode?.ToString() ?? "<none>"}";
    }
}
