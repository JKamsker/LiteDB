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

        public static SpatialPluginServices Attach(BaseLiteDB.LiteDatabase database, LiteDbPlugins.ILitePluginContext context)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (context == null) throw new ArgumentNullException(nameof(context));

            return _services.GetValue(database, _ => new SpatialPluginServices(database, context));
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

            services.UpdateDatabase(database);
            return _services.GetValue(database, _ => services);
        }
    }
}
