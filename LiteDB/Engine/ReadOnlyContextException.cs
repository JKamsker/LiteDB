using System.IO;

namespace LiteDB.Engine
{
    /// <summary>A rejected connection operation, not a storage I/O failure.</summary>
    internal sealed class ReadOnlyContextException : IOException
    {
        internal ReadOnlyContextException(string message) : base(message) { }
    }
}
