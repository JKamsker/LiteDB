using System;
using System.Reflection;
using LiteDB.Client.Shared;

namespace LiteDB.Tests.Safety
{
    /// <summary>
    /// Proof-overlay compatibility (revisions before #3072/#3077): reads the Shared connection state that
    /// later revisions expose as <c>SharedEngine.Pin</c> and <c>SharedMutexOwner.IsHeld</c>. Reflection
    /// throws when a field is renamed, like the other probes.
    /// </summary>
    internal static class SharedRevisionCompat
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>The connection's published pin, or null (dev: <c>SharedEngine.Pin</c>).</summary>
        internal static object Pin(SharedEngine engine) => Field(typeof(SharedEngine), "_pin").GetValue(engine);

        /// <summary>Whether any thread of the connection owns the mutex now (dev: <c>SharedMutexOwner.IsHeld</c>).</summary>
        internal static bool IsHeld(SharedMutexOwner owner)
        {
            var sync = Field(typeof(SharedMutexOwner), "_sync").GetValue(owner);
            lock (sync) return Field(typeof(SharedMutexOwner), "_owner").GetValue(owner) != null;
        }

        private static FieldInfo Field(Type type, string name) =>
            type.GetField(name, Instance) ?? throw new InvalidOperationException($"{type.Name}.{name} was renamed; update the probe.");
    }
}
