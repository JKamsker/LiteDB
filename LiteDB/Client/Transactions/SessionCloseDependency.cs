using System;

namespace LiteDB
{
    /// <summary>Detects close reentry from a native holder without keeping its facade alive.</summary>
    internal sealed class SessionCloseDependency : IDisposable
    {
        [ThreadStatic] private static SessionCloseDependency _current;
        private readonly object _token;
        private readonly SessionCloseDependency _previous;
        internal SessionCloseDependency(object token)
        { _token = token; _previous = _current; _current = this; }
        internal static bool Contains(object token)
        {
            for (var scope = _current; scope != null; scope = scope._previous)
                if (ReferenceEquals(scope._token, token)) return true;
            return false;
        }
        public void Dispose() => _current = _previous;
    }
}
