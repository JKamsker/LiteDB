using System;
using System.Threading;

namespace LiteDB.Plugins
{
    internal static class PluginExpressionScope
    {
        private sealed class ScopeToken : IDisposable
        {
            private readonly ILitePluginContext _previous;
            private bool _disposed;

            public ScopeToken(ILitePluginContext previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _current.Value = _previous;
                _disposed = true;
            }
        }

        private static readonly AsyncLocal<ILitePluginContext> _current = new AsyncLocal<ILitePluginContext>();

        public static ILitePluginContext Current => _current.Value;

        public static IDisposable Enter(ILitePluginContext context)
        {
            var previous = _current.Value;
            _current.Value = context;
            return new ScopeToken(previous);
        }
    }
}
