using System;
using LiteDB.Plugins.Query;

namespace LiteDB.Plugins
{
    /// <summary>
    /// Provides a minimal, per-process fallback plugin context for legacy call paths that
    /// must operate without an active <see cref="LiteDatabase"/> instance.
    /// </summary>
    internal static class PluginContextFallbacks
    {
        private static readonly DefaultPluginContext _context =
            new DefaultPluginContext(new ConnectionString(), NullServiceProvider.Instance, NullLogger.Instance);

        internal static ILitePluginContext Context => _context;

        internal static IExpressionRegistry Expressions => _context.Expressions;
        internal static IQueryOperatorRegistry QueryOperators => _context.QueryOperators;
        internal static ILinqResolverRegistry LinqResolvers => _context.LinqResolvers;
    }
}
