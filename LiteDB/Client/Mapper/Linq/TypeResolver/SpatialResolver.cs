using System;
using System.Reflection;
using LiteDB.Spatial;

namespace LiteDB
{
    internal class SpatialResolver : ITypeResolver
    {
        public string ResolveMethod(MethodInfo method)
        {
            if (method == null)
            {
                return null;
            }

            if (method.DeclaringType == typeof(SpatialExpressions) || method.DeclaringType == typeof(Spatial.Spatial))
            {
                switch (method.Name)
                {
                    case nameof(SpatialExpressions.Near):
                        return ResolveNearPattern(method);
                    case nameof(SpatialExpressions.Within):
                        if (MatchesTwoParameterSignature(method, typeof(GeoShape), typeof(GeoPolygon)))
                        {
                            return "SPATIAL_WITHIN(@0, @1)";
                        }

                        break;
                    case nameof(SpatialExpressions.Intersects):
                        if (MatchesTwoParameterSignature(method, typeof(GeoShape), typeof(GeoShape)))
                        {
                            return "SPATIAL_INTERSECTS(@0, @1)";
                        }

                        break;
                    case nameof(SpatialExpressions.Contains):
                        if (MatchesTwoParameterSignature(method, typeof(GeoShape), typeof(GeoPoint)))
                        {
                            return "SPATIAL_CONTAINS(@0, @1)";
                        }

                        break;
                    case nameof(SpatialExpressions.WithinBoundingBox):
                        if (MatchesBoundingBoxSignature(method))
                        {
                            return "SPATIAL_WITHIN_BOX(@0, @1, @2, @3, @4)";
                        }

                        break;
                }
            }

            return null;
        }

        public string ResolveMember(MemberInfo member) => null;

        public string ResolveCtor(ConstructorInfo ctor) => null;

        private static bool MatchesTwoParameterSignature(MethodInfo method, Type first, Type second)
        {
            var parameters = method.GetParameters();

            return parameters.Length == 2 &&
                   first.IsAssignableFrom(parameters[0].ParameterType) &&
                   second.IsAssignableFrom(parameters[1].ParameterType);
        }

        private static bool MatchesBoundingBoxSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (parameters.Length != 5)
            {
                return false;
            }

            return typeof(GeoPoint).IsAssignableFrom(parameters[0].ParameterType) &&
                   parameters[1].ParameterType == typeof(double) &&
                   parameters[2].ParameterType == typeof(double) &&
                   parameters[3].ParameterType == typeof(double) &&
                   parameters[4].ParameterType == typeof(double);
        }

        private string ResolveNearPattern(MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (parameters.Length == 3 &&
                typeof(GeoPoint).IsAssignableFrom(parameters[0].ParameterType) &&
                typeof(GeoPoint).IsAssignableFrom(parameters[1].ParameterType) &&
                parameters[2].ParameterType == typeof(double))
            {
                var formula = Spatial.Spatial.Options.Distance.ToString();
                return $"SPATIAL_NEAR(@0, @1, @2, '{formula}')";
            }

            if (parameters.Length == 4 &&
                typeof(GeoPoint).IsAssignableFrom(parameters[0].ParameterType) &&
                typeof(GeoPoint).IsAssignableFrom(parameters[1].ParameterType) &&
                parameters[2].ParameterType == typeof(double))
            {
                return "SPATIAL_NEAR(@0, @1, @2, @3)";
            }

            throw new NotSupportedException("Unsupported overload for spatial Near expression");
        }
    }
}
