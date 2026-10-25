using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LiteDB.Engine
{
    internal partial class DiskService
    {
        private WalCommitGroup _commitGroup;
        internal int CommitReadVersionLimit => Volatile.Read(ref _commitGroup)?.ReadVersionLimit ?? int.MaxValue;
        internal IEnumerable<int> PendingCommitVersions => Volatile.Read(ref _commitGroup)?.PendingVersions ?? System.Linq.Enumerable.Empty<int>();

        internal WalCommitGroup CommitGroup
        {
            get
            {
#if NET8_0_OR_GREATER
                if (_durableCommits && ChecksumsEnabled && _sharedDurability == null && _signals == null && !_logFlushDegraded &&
                    !LogSyncUnverified && ((ChecksummedWalFactory)_logFactory).IsFile &&
                    ((ChecksummedWalStream)_writer.Value).RawStream.GetType() == typeof(FileStream))
                {
                    var group = Volatile.Read(ref _commitGroup);
                    if (group == null)
                    {
                        group = new WalCommitGroup(this, _state);
                        group = Interlocked.CompareExchange(ref _commitGroup, group, null) ?? group;
                    }
                    return group;
                }
#endif
                return null;
            }
        }

        internal void FlushCommitGroup()
        {
            try
            {
                NativeFileSync.FlushDevice((FileStream)((ChecksummedWalStream)_writer.Value).RawStream);
                _logSyncProven = true;
            }
            catch (System.Exception error) when (IsDurableFlushUnsupported(error))
            {
                MarkLogFlushDegraded(error);
            }
            SyncLogDirectory();
        }
    }
}
