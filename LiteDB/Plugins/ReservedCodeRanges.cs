using System;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Defines reserved identifier ranges owned by built-in plugins.
    /// </summary>
    internal static class ReservedCodeRanges
    {
        public const string VectorPluginId = "LiteDB.Vector";
        public const string VectorStrategyKind = "vector";
        public const string VectorIndexKindPrefix = "vector.";
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

        public static void EnsureUnique(bool conflictDetected, string identifierDescription, string existingPluginId, string incomingPluginId)
        {
            if (conflictDetected)
            {
                throw new InvalidOperationException($"{identifierDescription} is already registered by plugin '{existingPluginId}' and cannot be claimed by '{incomingPluginId}'.");
            }
        }

        public static void EnsurePluginOwnsReservedIdentifier(string pluginId, string value, string reservedValue, string owner, string identifierKind)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (string.Equals(value, reservedValue, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pluginId, owner, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{identifierKind} '{value}' is reserved for plugin '{owner}'. '{pluginId}' cannot claim it.");
            }
        }

        public static void EnsurePluginOwnsReservedPrefix(string pluginId, string value, string reservedPrefix, string owner, string identifierKind)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (value.StartsWith(reservedPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pluginId, owner, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{identifierKind} '{value}' is reserved for plugin '{owner}'. '{pluginId}' cannot claim it.");
            }
        }
    }
}
