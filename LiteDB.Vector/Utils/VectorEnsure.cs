using LiteDB;
using LiteDB.Vector;
using System.Diagnostics;

namespace LiteDB.Vector.Utils
{
    internal static class VectorEnsure
    {
        internal static void ENSURE(bool conditional, string message = null)
        {
            if (!conditional)
            {
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                throw new LiteException(0, message ?? "Vector invariant failed.");
            }
        }

        internal static void ENSURE(bool conditional, string format, params object[] args)
        {
            ENSURE(conditional, string.Format(format, args));
        }

        internal static void ENSURE(bool ifTest, bool conditional, string message = null)
        {
            if (ifTest)
            {
                ENSURE(conditional, message);
            }
        }

        internal static double NormalizeMaxDistance(double maxDistance, byte? metric)
        {
            if (!metric.HasValue)
            {
                return maxDistance;
            }

            var metricValue = (VectorDistanceMetric)metric.Value;

            if (metricValue != VectorDistanceMetric.DotProduct)
            {
                return maxDistance;
            }

            if (double.IsNaN(maxDistance) || double.IsInfinity(maxDistance))
            {
                return maxDistance;
            }

            return maxDistance > 0d ? -maxDistance : maxDistance;
        }
    }
}
