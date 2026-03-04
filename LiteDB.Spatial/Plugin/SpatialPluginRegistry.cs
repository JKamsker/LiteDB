extern alias LiteDbBase;

using System;
using System.Runtime.CompilerServices;
using BaseLiteDB = LiteDbBase::LiteDB;
using LiteDbPlugins = LiteDbBase::LiteDB.Plugins;

namespace LiteDB.Spatial.Plugin
{
    internal static class SpatialPluginRegistry
    {
        private static readonly ConditionalWeakTable<BaseLiteDB.LiteDatabase, SpatialPluginServices> _services = new ConditionalWeakTable<BaseLiteDB.LiteDatabase, SpatialPluginServices>();
        private static readonly ConditionalWeakTable<LiteDbPlugins.ILitePluginContext, SpatialPluginServices> _servicesByContext = new ConditionalWeakTable<LiteDbPlugins.ILitePluginContext, SpatialPluginServices>();

        public static SpatialPluginServices Attach(BaseLiteDB.LiteDatabase database, LiteDbPlugins.ILitePluginContext context)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var services = _servicesByContext.GetValue(context, _ => new SpatialPluginServices(database, context));

            return _services.GetValue(database, _ => services);
        }

        public static bool TryGetServices(BaseLiteDB.LiteDatabase database, out SpatialPluginServices services)
        {
            if (database == null)
            {
                services = null;
                return false;
            }

            return _services.TryGetValue(database, out services);
        }

        public static SpatialPluginServices AttachExisting(BaseLiteDB.LiteDatabase database, SpatialPluginServices services)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (services == null) throw new ArgumentNullException(nameof(services));

            return _services.GetValue(database, _ => services);
        }
    }
}
