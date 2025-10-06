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

            if (method.DeclaringType == typeof(SpatialExpressions))
            {
                return ResolveExpressions(method);
            }

            if (method.DeclaringType == typeof(Spatial.Spatial))
            {
                return ResolveSpatial(method);
            }

            return null;
        }

        public string ResolveMember(MemberInfo member) => null;

        public string ResolveCtor(ConstructorInfo ctor) => null;

        private string ResolveExpressions(MethodInfo method)
        {
            switch (method.Name)
            {
                case nameof(SpatialExpressions.Near):
                    return ResolveNearPattern(method);
                case nameof(SpatialExpressions.Within):
                    return "SPATIAL_WITHIN(@0, @1)";
                case nameof(SpatialExpressions.Intersects):
                    return "SPATIAL_INTERSECTS(@0, @1)";
                case nameof(SpatialExpressions.Contains):
                    return "SPATIAL_CONTAINS(@0, @1)";
                case nameof(SpatialExpressions.WithinBoundingBox):
                    return "SPATIAL_WITHIN_BOX(@0, @1, @2, @3, @4)";
            }

            return null;
        }

        private string ResolveSpatial(MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (method.Name == nameof(Spatial.Spatial.Near) && parameters.Length == 3 && parameters[0].ParameterType == typeof(GeoPoint))
            {
                return "SPATIAL_NEAR(@0, @1, @2)";
            }

            if (method.Name == nameof(Spatial.Spatial.Within) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
            {
                return "SPATIAL_WITHIN(@0, @1)";
            }

            if (method.Name == nameof(Spatial.Spatial.Intersects) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
            {
                return "SPATIAL_INTERSECTS(@0, @1)";
            }

            if (method.Name == nameof(Spatial.Spatial.Contains) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
            {
                return "SPATIAL_CONTAINS(@0, @1)";
            }

            return null;
        }

        private string ResolveNearPattern(MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (parameters.Length == 3)
            {
                var formula = Spatial.Spatial.Options.Distance.ToString();
                return $"SPATIAL_NEAR(@0, @1, @2, '{formula}')";
            }

            if (parameters.Length == 4)
            {
                return "SPATIAL_NEAR(@0, @1, @2, @3)";
            }

            throw new NotSupportedException("Unsupported overload for SpatialExpressions.Near");
        }
    }
}
