#nullable enable

namespace LiteDB.Spatial;

public static class SpatialCartesian3D
{
    public static Cartesian3DEngine CreateEngine(
        SpatialIndexOptions? options = null,
        BoundingBox? coordinateSpace = null,
        string geometryFieldName = SpatialCollectionDescriptor.DefaultGeometryFieldName)
    {
        return new Cartesian3DEngine(options, coordinateSpace, geometryFieldName);
    }

    public static SpatialCollectionDescriptor CreateDescriptor(
        string collectionName,
        string geometryFieldName,
        SpatialIndexOptions? options = null,
        BoundingBox? coordinateSpace = null)
    {
        var engine = CreateEngine(options, coordinateSpace, geometryFieldName);
        return new SpatialCollectionDescriptor(collectionName, Cartesian3DEngine.EngineName, engine.Dimensions, geometryFieldName, engine.Options, engine);
    }
}
