using System;

#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Describes how a collection is configured for spatial indexing.
/// </summary>
public sealed class SpatialCollectionDescriptor : IEquatable<SpatialCollectionDescriptor>
{
    private readonly Lazy<ISpatialEngine>? _engineAccessor;

    /// <summary>
    /// Default geometry field name used when metadata predates geometry persistence.
    /// </summary>
    public const string DefaultGeometryFieldName = "_geometry";

    /// <summary>
    /// Initializes a new instance of the <see cref="SpatialCollectionDescriptor"/> class.
    /// </summary>
    /// <param name="collectionName">The name of the collection.</param>
    /// <param name="engineName">The engine name responsible for indexing the collection.</param>
    /// <param name="dimensions">The spatial dimensionality supported by the engine.</param>
    /// <param name="geometryFieldName">The document field that contains the geometry value.</param>
    /// <param name="options">The index options associated with the collection.</param>
    /// <param name="settings">Optional engine-specific settings persisted alongside the descriptor.</param>
    /// <param name="engine">Optional runtime engine instance attached to the descriptor.</param>
    /// <param name="engineFactory">Optional factory that lazily produces the runtime engine.</param>
    public SpatialCollectionDescriptor(
        string collectionName,
        string engineName,
        int dimensions,
        string geometryFieldName,
        SpatialIndexOptions options,
        SpatialEngineSettings? settings = null,
        ISpatialEngine? engine = null,
        Func<ISpatialEngine>? engineFactory = null)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("Collection name must be provided.", nameof(collectionName));
        }

        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("Engine name must be provided.", nameof(engineName));
        }

        if (dimensions is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Spatial collections must be either two- or three-dimensional.");
        }

        if (engine is not null && engineFactory is not null)
        {
            throw new ArgumentException("Provide either an engine instance or an engine factory, not both.", nameof(engineFactory));
        }

        CollectionName = collectionName;
        EngineName = engineName;
        Dimensions = dimensions;
        GeometryFieldName = string.IsNullOrWhiteSpace(geometryFieldName) ? DefaultGeometryFieldName : geometryFieldName;
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Settings = settings ?? SpatialEngineSettings.Empty;

        if (engine is not null)
        {
            EnsureEngineCompatibility(engine);
            _engineAccessor = new Lazy<ISpatialEngine>(() => engine, isThreadSafe: true);
            _ = _engineAccessor.Value;
        }
        else if (engineFactory is not null)
        {
            _engineAccessor = new Lazy<ISpatialEngine>(() =>
            {
                var created = engineFactory();
                if (created is null)
                {
                    throw new SpatialMetadataException($"Engine factory for collection '{CollectionName}' produced a null instance.");
                }

                EnsureEngineCompatibility(created);
                return created;
            }, isThreadSafe: true);
        }
    }

    /// <summary>
    /// Gets the name of the collection that owns this descriptor.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// Gets the engine name used to service spatial queries for the collection.
    /// </summary>
    public string EngineName { get; }

    /// <summary>
    /// Gets the spatial dimensionality supported by the collection.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Gets the document field where geometries are stored.
    /// </summary>
    public string GeometryFieldName { get; }

    /// <summary>
    /// Gets the index options associated with the collection.
    /// </summary>
    public SpatialIndexOptions Options { get; }

    /// <summary>
    /// Gets additional metadata describing engine-specific configuration.
    /// </summary>
    public SpatialEngineSettings Settings { get; }

    /// <summary>
    /// Gets the runtime engine instance when attached.
    /// </summary>
    public ISpatialEngine? Engine => _engineAccessor is { IsValueCreated: true } accessor ? accessor.Value : null;

    /// <summary>
    /// Gets a value indicating whether a runtime engine has been attached.
    /// </summary>
    public bool HasEngine => _engineAccessor?.IsValueCreated == true;

    /// <summary>
    /// Gets a value indicating whether a runtime engine can be materialized lazily.
    /// </summary>
    public bool HasEngineFactory => _engineAccessor is not null;

    /// <summary>
    /// Gets the expected number of elements within the serialized bounding box.
    /// </summary>
    public int ExpectedBoundingBoxLength => Dimensions * 2;

    /// <summary>
    /// Attempts to resolve the runtime engine, materializing it if a factory was registered.
    /// </summary>
    /// <param name="engine">Receives the runtime engine when available.</param>
    /// <returns><c>true</c> if an engine or factory is associated with the descriptor; otherwise, <c>false</c>.</returns>
    public bool TryGetEngine(out ISpatialEngine engine)
    {
        if (_engineAccessor is null)
        {
            engine = null!;
            return false;
        }

        try
        {
            engine = _engineAccessor.Value;
            return true;
        }
        catch (SpatialMetadataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SpatialMetadataException($"Failed to materialize runtime engine for collection '{CollectionName}'. {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a new descriptor with the provided engine instance attached.
    /// </summary>
    /// <param name="engine">The runtime engine.</param>
    /// <returns>A descriptor that references the supplied engine.</returns>
    public SpatialCollectionDescriptor WithEngine(ISpatialEngine engine)
    {
        if (engine is null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        EnsureEngineCompatibility(engine);
        return new SpatialCollectionDescriptor(CollectionName, EngineName, Dimensions, GeometryFieldName, Options, Settings, engine);
    }

    /// <summary>
    /// Creates a new descriptor that materializes its runtime engine on demand.
    /// </summary>
    /// <param name="engineFactory">Factory responsible for producing the runtime engine.</param>
    /// <returns>A descriptor that invokes the factory lazily.</returns>
    public SpatialCollectionDescriptor WithEngineFactory(Func<ISpatialEngine> engineFactory)
    {
        if (engineFactory is null)
        {
            throw new ArgumentNullException(nameof(engineFactory));
        }

        return new SpatialCollectionDescriptor(CollectionName, EngineName, Dimensions, GeometryFieldName, Options, Settings, engineFactory: engineFactory);
    }

    /// <summary>
    /// Ensures that the provided bounding box matches the descriptor dimensionality.
    /// </summary>
    /// <param name="box">The bounding box to validate.</param>
    public void EnsureCompatible(BoundingBox box)
    {
        if (box.Dimensions != Dimensions)
        {
            throw new SpatialMetadataException($"Collection '{CollectionName}' is configured for {Dimensions}D geometry but encountered a {box.Dimensions}D bounding box.");
        }
    }

    /// <summary>
    /// Ensures that a stored bounding box representation matches the descriptor dimensionality.
    /// </summary>
    /// <param name="valueCount">The number of serialized bounding box values.</param>
    public void EnsureCompatible(int valueCount)
    {
        if (valueCount != ExpectedBoundingBoxLength)
        {
            throw new SpatialMetadataException($"Collection '{CollectionName}' is configured for {Dimensions}D geometry but {Options.BoundingBoxFieldName} contains {valueCount} values.");
        }
    }

    /// <inheritdoc />
    public bool Equals(SpatialCollectionDescriptor? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(CollectionName, other.CollectionName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(EngineName, other.EngineName, StringComparison.Ordinal)
            && Dimensions == other.Dimensions
            && string.Equals(GeometryFieldName, other.GeometryFieldName, StringComparison.Ordinal)
            && Options.Equals(other.Options)
            && Settings.Equals(other.Settings);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SpatialCollectionDescriptor descriptor && Equals(descriptor);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(CollectionName);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(EngineName);
            hash = (hash * 397) ^ Dimensions;
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(GeometryFieldName);
            hash = (hash * 397) ^ Options.GetHashCode();
            hash = (hash * 397) ^ Settings.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Collection={CollectionName}, Engine={EngineName}, Dimensions={Dimensions}, GeometryField={GeometryFieldName}, Options=({Options})";
    }

    private void EnsureEngineCompatibility(ISpatialEngine engine)
    {
        if (!string.Equals(engine.Name, EngineName, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Engine '{engine.Name}' does not match descriptor engine '{EngineName}'.", nameof(engine));
        }

        if (engine.Dimensions != Dimensions)
        {
            throw new ArgumentException($"Engine '{engine.Name}' reports {engine.Dimensions} dimensions but descriptor expects {Dimensions}.", nameof(engine));
        }

        if (!engine.Options.Equals(Options))
        {
            throw new ArgumentException("Engine options do not match the descriptor options.", nameof(engine));
        }

        if (Settings.Domain is { } domain)
        {
            if (engine is not ICartesianSpatialEngine cartesian)
            {
                throw new ArgumentException("Descriptor domain metadata is only supported for Cartesian engines.", nameof(engine));
            }

            if (!cartesian.Domain.Equals(domain))
            {
                throw new ArgumentException("Engine domain does not match the descriptor settings.", nameof(engine));
            }
        }

        if (Settings.DistanceMode is { } mode)
        {
            if (engine is not IGeographicSpatialEngine geographic)
            {
                throw new ArgumentException("Descriptor distance mode metadata is only supported for geographic engines.", nameof(engine));
            }

            if (geographic.DistanceMode != mode)
            {
                throw new ArgumentException("Engine distance mode does not match the descriptor settings.", nameof(engine));
            }
        }
    }
}
