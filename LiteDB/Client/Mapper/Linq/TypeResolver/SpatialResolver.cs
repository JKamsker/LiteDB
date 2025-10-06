using System;
using System.Reflection;
using LiteDB.Spatial;
using SpatialMethods = LiteDB.Spatial.Spatial;

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

            if (method.DeclaringType == typeof(SpatialMethods))
            {
                return ResolveLegacy(method);
            }

            return null;
        }

        public string ResolveMember(MemberInfo member) => null;

        public string ResolveCtor(ConstructorInfo ctor) => null;

        private string ResolveExpressions(MethodInfo method)
        {
            return method.Name switch
            {
                nameof(SpatialExpressions.Near) => ResolveExpressionsNear(method),
                nameof(SpatialExpressions.Within) => "SPATIAL_WITHIN(@0, @1)",
                nameof(SpatialExpressions.Intersects) => "SPATIAL_INTERSECTS(@0, @1)",
                nameof(SpatialExpressions.Contains) => "SPATIAL_CONTAINS(@0, @1)",
                nameof(SpatialExpressions.WithinBoundingBox) => "SPATIAL_WITHIN_BOX(@0, @1, @2, @3, @4)",
                _ => null
            };
        }

        private string ResolveLegacy(MethodInfo method)
        {
            var parameters = method.GetParameters();

            if (method.Name == nameof(SpatialMethods.Near) && parameters.Length == 3 && parameters[0].ParameterType == typeof(GeoPoint))
            {
                var formula = Spatial.Spatial.Options.Distance.ToString();
                return $"SPATIAL_NEAR(@0, @1, @2, '{formula}')";
            }

            if (method.Name == nameof(SpatialMethods.Within) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
            {
                return "SPATIAL_WITHIN(@0, @1)";
            }

            if (method.Name == nameof(SpatialMethods.Intersects) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
            {
                return "SPATIAL_INTERSECTS(@0, @1)";
            }

            if (method.Name == nameof(SpatialMethods.Contains) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
            {
                return "SPATIAL_CONTAINS(@0, @1)";
            }

            return null;
        }

        private string ResolveExpressionsNear(MethodInfo method)
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
