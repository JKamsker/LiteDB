#nullable enable

namespace LiteDB.Spatial;

/// <summary>
/// Convenience helpers for configuring collections that rely on the geographic engine.
/// </summary>
public static class SpatialGeographic
{
    public static GeographicEngine CreateEngine(
        SpatialIndexOptions? options = null,
        string geometryFieldName = SpatialCollectionDescriptor.DefaultGeometryFieldName)
    {
        return new GeographicEngine(options, geometryFieldName);
    }

    public static SpatialCollectionDescriptor CreateDescriptor(
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null)
    {
        var engine = CreateEngine(options, geometryFieldName);
        return new SpatialCollectionDescriptor(collectionName, GeographicEngine.EngineName, engine.Dimensions, geometryFieldName, engine.Options, engine);
    }
}
