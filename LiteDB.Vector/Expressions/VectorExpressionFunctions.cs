using LiteDB;

namespace LiteDB.Vector
{
    /// <summary>
    /// Hosts expression function adapters invoked by the query engine.
    /// </summary>
    public static class VectorExpressionFunctions
    {
        /// <summary>
        /// Computes the vector similarity for use in <c>VECTOR_SIM(field, target)</c> expressions.
        /// </summary>
        public static BsonValue VectorSim(BsonDocument root, Collation collation, BsonDocument parameters, BsonValue left, BsonValue right)
        {
            return VectorExpressionMethods.VectorSim(left, right);
        }
    }
}
