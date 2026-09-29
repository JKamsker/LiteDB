using System;
using LiteDB.Engine;

namespace LiteDB.Client.Direct
{
    internal static class DirectEngineSettings
    {
        internal static void RequireCompatible(DirectEnginePool.Entry entry, EngineSettings requested)
        {
            var current = entry.Settings;
            // Rebuild updates this live settings object, including on publication
            // followed by cleanup failure. Never authenticate against an old password.
            if (!string.Equals(current.Password, requested.Password, StringComparison.Ordinal))
                throw new LiteException(LiteException.INVALID_PASSWORD, "Invalid password for the open Direct database.");
            if (entry.Engine.IsDisposed)
                throw DirectEnginePool.Conflict(requested, "The Direct engine stopped. Dispose all its database instances before reopening.");
            if (current.ReadOnly != requested.ReadOnly || current.DurableCommits != requested.DurableCommits ||
                current.AllowHostLocalAdmissionFallback != requested.AllowHostLocalAdmissionFallback ||
                current.CompactStorage != requested.CompactStorage || current.LegacyIndexScan != requested.LegacyIndexScan ||
                current.Upgrade != requested.Upgrade || current.AutoRebuild != requested.AutoRebuild ||
                current.RejectInvalidLocalTime != requested.RejectInvalidLocalTime ||
                current.GetCacheSize() != requested.GetCacheSize() || current.TransactionPageLimit != requested.TransactionPageLimit ||
                current.IndexMigrationLimitSize != requested.IndexMigrationLimitSize ||
                current.ReadTransform != requested.ReadTransform || current.LocalTimeZone != requested.LocalTimeZone ||
                (requested.Collation != null && requested.Collation.ToString() !=
                    (current.Collation?.ToString() ?? entry.InitialCollation)))
                throw DirectEnginePool.Conflict(requested, "Connection settings conflict with the open Direct engine. Use compatible settings or close all existing owners.");
            // InitialSize only affects creation; mapper and client state are per owner.
        }
    }
}
