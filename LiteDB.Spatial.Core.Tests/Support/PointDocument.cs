extern alias LiteDbBase;

using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Core.Tests.Support;

public sealed class PointDocument
{
    [BaseLiteDB.BsonId]
    public BaseLiteDB.ObjectId Id { get; set; }
    public GeoPoint Position { get; set; }
}
