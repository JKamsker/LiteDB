using System;
using System.Runtime.CompilerServices;
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
        private static readonly ConditionalWeakTable<ILitePluginContext, BsonTypeRegistry> _registries = new ConditionalWeakTable<ILitePluginContext, BsonTypeRegistry>();
        private static readonly ILitePluginContext _defaultContext = CreateDefaultContext();

        internal static BsonTypeRegistry GetRegistry(ILitePluginContext context)
        {
            var target = context ?? _defaultContext;
            return _registries.GetValue(target, static ctx => new BsonTypeRegistry(ctx.BsonTypes));
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

        [Obsolete("Global fallback registries have been removed; registries are scoped per ILitePluginContext.")]
        internal static void ReplaceFallbackRegistry(ILitePluginContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _registries.Remove(context);
            _registries.GetValue(context, static ctx => new BsonTypeRegistry(ctx.BsonTypes));
        }

        private static ILitePluginContext CreateDefaultContext()
        {
            return new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);
        }
    }
}
