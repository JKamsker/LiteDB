using System;
using LiteDB.Document.Bson;
using LiteDB.Plugins;
using LiteDB.Plugins.Bson;

namespace LiteDB
{
    /// <summary>
    /// All supported BsonTypes in sort order
    /// </summary>
    public enum BsonType : byte
    {
        MinValue = 0,

        Null = 1,

        Int32 = 2,
        Int64 = 3,
        Double = 4,
        Decimal = 5,

        String = 6,

        Document = 7,
        Array = 8,

        Binary = 9,
        ObjectId = 10,
        Guid = 11,

        Boolean = 12,
        DateTime = 13,

        MaxValue = 14,

        [Obsolete("Vector serialization is provided by the LiteDB.Vector plugin via the BSON type registry.")]
        Vector = 100,
    }

    internal static class BsonTypeResolver
    {
        private static readonly object _sync = new object();
        private static BsonTypeRegistry _fallbackRegistry = CreateFallbackRegistry();

        internal static BsonTypeRegistry GetRegistry(ILitePluginContext context)
        {
            if (context == null)
            {
                lock (_sync)
                {
                    return _fallbackRegistry;
                }
            }

            return new BsonTypeRegistry(context.BsonTypes);
        }

        internal static bool TryGet(ILitePluginContext context, byte typeCode, out BsonTypeRegistration registration)
        {
            var registry = GetRegistry(context);
            return registry.TryGet(typeCode, out registration);
        }

        internal static bool TryGet(ILitePluginContext context, BsonType type, out BsonTypeRegistration registration)
        {
            return TryGet(context, (byte)type, out registration);
        }

        internal static void ReplaceFallbackRegistry(ILitePluginContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (_sync)
            {
                _fallbackRegistry = new BsonTypeRegistry(context.BsonTypes);
            }
        }

        internal static void RegisterFallback(BsonTypeRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            lock (_sync)
            {
                _fallbackRegistry.RegisterFallback(registration);
            }
        }

        private static BsonTypeRegistry CreateFallbackRegistry()
        {
            return new BsonTypeRegistry(LiteDatabaseServices.Default.Context.BsonTypes);
        }
    }
}
