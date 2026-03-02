using System;
using LiteDB;
using LiteDB.Plugins.Query;
using LiteDB.Vector.Engine;
using LiteDB.Vector.Utils;

namespace LiteDB.Vector.Query
{
    /// <summary>
    /// Factory helpers for registering SQL function delegates exposed by the vector plugin.
    /// </summary>
    internal static class VectorSqlFunctions
    {
        public static SqlFunctionRegistration CreateVectorDistance(string pluginId)
        {
            if (pluginId == null)
            {
                throw new ArgumentNullException(nameof(pluginId));
            }

            return new SqlFunctionRegistration(
                pluginId,
                "VECTOR_DIST",
                args => ExecuteVectorDistance(args),
                minParameterCount: 2,
                maxParameterCount: 3);
        }

        public static SqlFunctionRegistration CreateVectorSimilarity(string pluginId)
        {
            if (pluginId == null)
            {
                throw new ArgumentNullException(nameof(pluginId));
            }

            return new SqlFunctionRegistration(
                pluginId,
                "VECTOR_SIM",
                args => ExecuteVectorSimilarity(args),
                minParameterCount: 2,
                maxParameterCount: 3);
        }

        private static BsonValue ExecuteVectorDistance(BsonValue[] arguments)
        {
            EnsureArgumentCount(arguments, "VECTOR_DIST", 2, 3);

            if (arguments.Length == 2)
            {
                return VectorExpressions.VectorDistance(arguments[0], arguments[1]);
            }

            var metric = ResolveMetric(arguments[2]);
            return VectorExpressions.VectorDistance(arguments[0], arguments[1], metric);
        }

        private static BsonValue ExecuteVectorSimilarity(BsonValue[] arguments)
        {
            EnsureArgumentCount(arguments, "VECTOR_SIM", 2, 3);

            if (arguments.Length == 2)
            {
                return VectorExpressions.VectorSimilarity(arguments[0], arguments[1]);
            }

            var metric = ResolveMetric(arguments[2]);
            return VectorExpressions.VectorSimilarity(arguments[0], arguments[1], metric);
        }

        private static void EnsureArgumentCount(BsonValue[] arguments, string functionName, int min, int max)
        {
            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            if (arguments.Length < min || (max >= 0 && arguments.Length > max))
            {
                throw new LiteException(0, $"{functionName} expects between {min} and {max} arguments.");
            }
        }

        private static VectorDistanceMetric? ResolveMetric(BsonValue candidate)
        {
            if (candidate == null || candidate.IsNull)
            {
                return null;
            }

            if (VectorMetricParser.TryParse(candidate, out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
