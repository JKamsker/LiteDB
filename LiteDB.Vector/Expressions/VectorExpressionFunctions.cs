using LiteDB;

namespace LiteDB.Vector
{
    internal static class VectorExpressionFunctions
    {
        public static BsonValue VECTOR_SIM(BsonDocument root, Collation collation, BsonDocument parameters, BsonValue left, BsonValue right)
        {
            return VectorExpressionMethods.VECTOR_SIM(left, right);
        }
    }
}
