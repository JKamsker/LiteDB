extern alias LiteDbBase;
using System;
using System.Linq;
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
    public sealed class SpatialPlugin : LiteDbPlugins.ILitePlugin, LiteDbPlugins.ILiteDatabaseHandleLifecycle
    {
        private const string LogPrefix = "[SpatialPlugin]";

        public void Initialize(BaseLiteDB.LiteDatabase database, LiteDbPlugins.ILitePluginContext context)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var services = SpatialPluginRegistry.Attach(database, context);

            RegisterExpressionFunctions(context.Expressions);

            context.LinqResolvers.Register(typeof(SpatialExpressions), _ => services.GetOrCreateResolver(typeof(SpatialExpressions)));
            context.LinqResolvers.Register(typeof(Spatial), _ => services.GetOrCreateResolver(typeof(Spatial)));

            context.QueryPlanner.AddRule(services.CreatePlanningRule(), order: 100);
            context.EnsureIndexInterceptors.Add(new SpatialEnsureIndexInterceptor(services), order: 100);
        }

        public void OnHandleCreated(BaseLiteDB.ILiteDatabase database)
        {
            if (database is not BaseLiteDB.LiteDatabase liteDatabase)
            {
                return;
            }

            var context = liteDatabase.Services?.Context;

            if (context != null)
            {
                SpatialPluginRegistry.Attach(liteDatabase, context);
            }
        }

        /// <summary>
        /// Writes diagnostic information about the spatial plugin configuration to the database logger.
        /// </summary>
        /// <param name="database">The database to inspect.</param>
        /// <param name="throwOnFailure">Throw an exception when the plugin is missing or no descriptors are registered.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
        public static void LogDiagnostics(BaseLiteDB.LiteDatabase database, bool throwOnFailure = false)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            var logger = database.Services?.Logger ?? LiteDbPlugins.NullLogger.Instance;

            if (!SpatialPluginRegistry.TryGetServices(database, out var services))
            {
                const string message = LogPrefix + " Spatial plugin is not attached to this database instance. Register `new SpatialPlugin()` when constructing LiteDatabase.";
                logger.Write(LiteDbPlugins.LogLevel.Warning, message);

                if (throwOnFailure)
                {
                    throw new BaseLiteDB.LiteException(BaseLiteDB.LiteException.INVALID_COMMAND, message);
                }

                return;
            }

            var descriptors = services.GetDescriptors().ToList();
            if (descriptors.Count == 0)
            {
                const string message = LogPrefix + " No spatial descriptors found. Call EnsureIndex(x => x.GeoField) after enabling the plugin.";
                logger.Write(LiteDbPlugins.LogLevel.Warning, message);

                if (throwOnFailure)
                {
                    throw new BaseLiteDB.LiteException(BaseLiteDB.LiteException.INVALID_COMMAND, message);
                }

                return;
            }

            foreach (var descriptor in descriptors)
            {
                var engineMessage = $"{LogPrefix} Collection='{descriptor.CollectionName}', Field='{descriptor.GeometryFieldName}', Engine='{descriptor.EngineName}', Dimensions={descriptor.Dimensions}.";
                logger.Write(LiteDbPlugins.LogLevel.Information, engineMessage);
            }
        }

        private static void RegisterExpressionFunctions(LiteDbPlugins.IExpressionRegistry registry)
        {
            Register("SPATIAL_NEAR", SpatialExpressionFunctions.InvokeNear);
            Register("SPATIAL_WITHIN", SpatialExpressionFunctions.InvokeWithin);
            Register("SPATIAL_INTERSECTS", SpatialExpressionFunctions.InvokeIntersects);
            Register("SPATIAL_CONTAINS", SpatialExpressionFunctions.InvokeContains);
            Register("SPATIAL_IN_BOX", SpatialExpressionFunctions.InvokeInBox);

            void Register<TDelegate>(string name, TDelegate implementation)
                where TDelegate : Delegate
            {
                registry.RegisterFunction
                (
                    name,
                    implementation,
                    BaseLiteDB.BsonExpressionType.Call,
                    convertScalarLeftToEnumerable: false,
                    isScalarResult: true
                );
            }
        }

        private sealed class SpatialEnsureIndexInterceptor : LiteDbPlugins.IEnsureIndexInterceptor
        {
            private readonly SpatialPluginServices _services;

            public SpatialEnsureIndexInterceptor(SpatialPluginServices services)
            {
                _services = services ?? throw new ArgumentNullException(nameof(services));
            }

            public bool TryHandleEnsureIndex(LiteDbPlugins.EnsureIndexContext context)
            {
                return _services.TryHandleEnsureIndex(context);
            }
        }
    }
}
