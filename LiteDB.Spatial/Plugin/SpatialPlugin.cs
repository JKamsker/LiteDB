extern alias LiteDbBase;

using System;
using LiteDB.Spatial.Plugin.Linq;
using LiteDB.Spatial.Plugin.QueryPlanning;
using LiteDB.Spatial.Plugin.Runtime;
using BaseLiteDB = LiteDbBase::LiteDB;
using LiteDbPlugins = LiteDbBase::LiteDB.Plugins;

namespace LiteDB.Spatial.Plugin
{
    /// <summary>
    /// Registers spatial infrastructure with the LiteDB plugin system.
    /// </summary>
    public sealed class SpatialPlugin : LiteDbPlugins.ILitePlugin
    {
        public void Initialize(BaseLiteDB.LiteDatabase database, LiteDbPlugins.ILitePluginContext context)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var services = SpatialPluginRegistry.Attach(database, context);

            RegisterExpressionFunctions(context.Expressions);

            context.LinqResolvers.Register(typeof(SpatialExpressions), _ => services.GetOrCreateResolver(typeof(SpatialExpressions)));
            context.LinqResolvers.Register(typeof(Spatial), _ => services.GetOrCreateResolver(typeof(Spatial)));

            context.QueryPlanner.AddRule(services.CreatePlanningRule(), order: 100);

            context.IndexInterceptors.Register(ctx => services.TryHandleEnsureIndex(ctx), order: 0);
        }

        private static void RegisterExpressionFunctions(LiteDbPlugins.IExpressionRegistry registry)
        {
            registry.RegisterFunction(
                "SPATIAL_NEAR",
                (Func<BaseLiteDB.BsonDocument, BaseLiteDB.Collation, BaseLiteDB.BsonDocument, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue>)SpatialExpressionFunctions.InvokeNear,
                BaseLiteDB.BsonExpressionType.Call,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            registry.RegisterFunction(
                "SPATIAL_WITHIN",
                (Func<BaseLiteDB.BsonDocument, BaseLiteDB.Collation, BaseLiteDB.BsonDocument, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue>)SpatialExpressionFunctions.InvokeWithin,
                BaseLiteDB.BsonExpressionType.Call,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            registry.RegisterFunction(
                "SPATIAL_INTERSECTS",
                (Func<BaseLiteDB.BsonDocument, BaseLiteDB.Collation, BaseLiteDB.BsonDocument, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue>)SpatialExpressionFunctions.InvokeIntersects,
                BaseLiteDB.BsonExpressionType.Call,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            registry.RegisterFunction(
                "SPATIAL_CONTAINS",
                (Func<BaseLiteDB.BsonDocument, BaseLiteDB.Collation, BaseLiteDB.BsonDocument, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue>)SpatialExpressionFunctions.InvokeContains,
                BaseLiteDB.BsonExpressionType.Call,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);

            registry.RegisterFunction(
                "SPATIAL_IN_BOX",
                (Func<BaseLiteDB.BsonDocument, BaseLiteDB.Collation, BaseLiteDB.BsonDocument, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue, BaseLiteDB.BsonValue>)SpatialExpressionFunctions.InvokeInBox,
                BaseLiteDB.BsonExpressionType.Call,
                convertScalarLeftToEnumerable: false,
                isScalarResult: true);
        }
    }
}
