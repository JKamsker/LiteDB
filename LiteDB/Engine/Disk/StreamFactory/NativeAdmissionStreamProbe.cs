#if DEBUG || TESTING
using System;
using System.IO;
using System.Runtime.InteropServices;
using LiteDB.Client.Shared;
using Microsoft.Win32.SafeHandles;

namespace LiteDB.Engine
{
    // Observe real FileStream construction/finalization without adding an owner
    // edge or a separate finalizable wrapper to the production engine graph.
    internal sealed class NativeAdmissionStreamProbe : FileStream
    {
        [ThreadStatic]
        internal static Func<string, bool, Action<string>> Attach;
        private readonly Action<string> _observe;
        internal readonly string Filename;

        private NativeAdmissionStreamProbe(string path, FileMode mode, FileAccess access,
            FileShare share, int bufferSize, FileOptions options)
            : base(path, mode, access, share, bufferSize, options)
        {
            Filename = path;
            LiteDB.Utils.Reachability.FaultPoint("Attach");
            _observe = Attach?.Invoke(path, access != FileAccess.Read);
        }

        private NativeAdmissionStreamProbe(SafeFileHandle handle, string path, FileAccess access, int bufferSize)
            : base(handle, access, bufferSize)
        {
            Filename = path;
            LiteDB.Utils.Reachability.FaultPoint("Attach");
            _observe = Attach?.Invoke(path, access != FileAccess.Read);
        }

        internal static FileStream Open(string path, FileMode mode, FileAccess access,
            FileShare share, int bufferSize, FileOptions options, bool admitted)
        {
            if (!admitted || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new NativeAdmissionStreamProbe(path, mode, access, share, bufferSize, options);
            var handle = DatabaseFileIdentity.Open(path, access == FileAccess.Read, mode == FileMode.OpenOrCreate);
            try { return new NativeAdmissionStreamProbe(handle, path, access, bufferSize); }
            catch { handle.Dispose(); throw; }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) _observe?.Invoke("finalizing");
            try { base.Dispose(disposing); }
            finally { _observe?.Invoke(disposing ? "disposed" : "finalized"); }
        }
    }
}
#endif
