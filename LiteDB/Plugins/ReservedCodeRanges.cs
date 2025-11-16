using System;

namespace LiteDB.Plugins
{
    internal static class ReservedCodeRanges
    {
        public const string VectorPluginId = "LiteDB.Vector";
        public const string VectorStrategyKind = "vector";
        public const string VectorIndexKind = "vector.hnsw";
        public const byte VectorBsonStart = 0x90;
        public const byte VectorBsonEnd = 0x9F;
        public const byte VectorPageStart = 0xE0;
        public const byte VectorPageEnd = 0xEF;

        public static void EnsurePluginOwnsReservedRange(string pluginId, byte value, byte reservedStart, byte reservedEnd, string owner, string identifierKind)
        {
            if (value >= reservedStart && value <= reservedEnd && !string.Equals(pluginId, owner, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{identifierKind} value 0x{value:X2} is reserved for plugin '{owner}'. '{pluginId}' cannot claim it.");
            }
        }

        public static void EnsureUnique<T>(bool conflictDetected, string identifierDescription, string existingPluginId, string incomingPluginId)
        {
            if (conflictDetected)
            {
                throw new InvalidOperationException($"{identifierDescription} is already registered by plugin '{existingPluginId}' and cannot be claimed by '{incomingPluginId}'.");
            }
        }
    }
}
