extern alias LiteDbBase;

using System;
using System.Reflection;
using LiteDB.Spatial;
using BaseLiteDB = LiteDbBase::LiteDB;

namespace LiteDB.Spatial.Plugin.Linq
{
    internal sealed class SpatialLinqResolver : BaseLiteDB.ITypeResolver
    {
        private readonly SpatialPluginServices _services;

        public SpatialLinqResolver(SpatialPluginServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public string ResolveMethod(MethodInfo method)
        {
            if (method == null)
            {
                return null;
            }

            if (method.DeclaringType == typeof(SpatialExpressions))
            {
                return ResolveSpatialExpressions(method);
            }

            if (method.DeclaringType == typeof(SpatialQueryableExtensions))
            {
                return ResolveQueryableExtensions(method);
            }

            return null;
        }

        public string ResolveMember(MemberInfo member)
        {
            return null;
        }

        public string ResolveCtor(ConstructorInfo ctor)
        {
            return null;
        }

        private static string ResolveSpatialExpressions(MethodInfo method)
        {
            var parameters = method.GetParameters();

            return method.Name switch
            {
                nameof(SpatialExpressions.Near) when parameters.Length == 3
                    => "SPATIAL_NEAR(@0, @1, @2, NULL)",
                nameof(SpatialExpressions.Near) when parameters.Length == 4
                    => "SPATIAL_NEAR(@0, @1, @2, @3)",
                nameof(SpatialExpressions.InBox) when parameters.Length == 2
                    => "SPATIAL_IN_BOX(@0, @1)",
                _ => null
            };
        }

        private static string ResolveQueryableExtensions(MethodInfo method)
        {
            return method.Name switch
            {
                nameof(SpatialQueryableExtensions.WhereNear) => "SPATIAL_NEAR(@1, @2, @3, @4)",
                nameof(SpatialQueryableExtensions.WhereWithinBox) => "SPATIAL_IN_BOX(@1, @2)",
                _ => null
            };
        }
    }
}