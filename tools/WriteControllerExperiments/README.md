# Queued write and IPC experiments

Research tool for PR #133. The production library is unchanged. Build against an
explicit production Release DLL (`TestingEnabled=false`) in a separate checkout:

```sh
dotnet build tools/WriteControllerExperiments -c Release \
  -p:LibraryPath=/absolute/path/LiteDB.dll -o artifacts_temp/write-controller
dotnet artifacts_temp/write-controller/WriteControllerExperiments.dll \
  bench queue 16 32 1 3 5 artifacts_temp/write-data
dotnet artifacts_temp/write-controller/WriteControllerExperiments.dll \
  safety artifacts_temp/unique-empty-safety-directory
```

`bench MODE CALLERS BATCH LINGER_MS WARMUP_SECONDS MEASURE_SECONDS DIRECTORY`
accepts `direct`, `shared`, `existing`, `queue`, or `ipc`. `existing` uses the
library's existing `CoordinatedEngine` in a separate server process. `ipc` runs the
new controller in a separate process. Each caller has one outstanding command,
a persistent IPC connection/session where applicable, and a dedicated caller
thread. Direct uses one shared database instance (also supported by origin/dev).
Shared uses one session per caller. All modes insert identical immutable commands
into the same indexed collection, with the default checkpoint policy and durable
commits enabled. There are no automatic retries. Warmup and measured writes use
unique IDs; two cold reopens check every payload, IDs, index results and sentinel.

`queue` with batch size 1 isolates writer serialization without batching. Batch 32
with zero linger drains commands already queued. A one-millisecond linger lets
an otherwise empty queue collect a group, trading low-load latency for throughput.
The group cap is 256 commands; the bounded queue defaults to 4096 commands and
rejects excess submissions immediately. Fixed-size commands insert an int64 ID,
int32 value and constant 64-character payload. This is intentionally a small-write
workload, not a general BSON API or arbitrary application callback executor.

## Transaction contract

**This is command coalescing into one atomic transaction, not independent engine
transactions sharing an fsync.** The dedicated writer begins a transaction,
serializes the submitted inserts, commits once, then acknowledges all requests.
One invalid duplicate rejects and rolls back its entire batch. The caller must
accept that grouping policy. It cannot submit an already-open transaction or
rely on a separate snapshot/rollback boundary per command. Input commands are
immutable; the writer owns all database access and transaction execution.

No success precedes `Commit()` returning. A commit/I/O/rollback failure stops the
controller and fails every affected/pending request. An uncertain commit is never
reported as a successful rollback. Shutdown rejects new requests and drains
accepted commands. The pipe is local and same-user only, with a fixed 12-byte
request and correlated 12-byte response. EOF in a partial request submits nothing.
Disconnection after a complete request is **indeterminate**: the write may commit
without its acknowledgement arriving. There is no automatic failover, retry,
deduplication, durable inbox, security boundary against malicious same-user code,
or takeover protocol. Reconcile the request ID before retrying; a durable inbox
would be needed for a stronger retry contract.

The IPC process owns a Direct database for its lifetime. Other processes cannot
open the database independently in Direct or Shared mode while it runs. This
proof's protocol only serves writes; a usable multiprocess service would also
need a read/snapshot API or integration with the existing coordinator. It is not
a transparent replacement for Shared connections.

Existing collection/header locks cover commit and fsync. Moving independent
transactions behind a shared flush would require changing publication and lock
lifetimes, handling a whole cohort after failed fsync, and validating recovery of
separate confirmations. Same-collection writers cannot all reach commit while
one holds the collection lock. This prototype investigates the higher-level
option that can batch that workload without changing the WAL protocol.

## Evidence and limitations

`scripts/benchmark-write-controller.py` alternates case order, pins library
revision/hash, and retains all results. Run builds and safety checks before timing;
do not benchmark concurrently with them. Report median and range of independent
processes. Latency includes queueing/IPC/commit; p99 is per process. Client allocation
excludes the separate server, so cross-process allocation is not comparable to
in-process allocation. CPU includes separate client/server counters. A 25ms sampler
estimates peak WAL bytes (a lower bound); it does not measure all bytes written.
Drain time is measured separately. IPC host commit counts include warmup; queue
commit deltas cover only measurement. Startup/connection setup and cold verification
are excluded from throughput. Raw outputs retain these distinctions.

Safety probes force a batch to block at durable sync and check no early success,
exactly one commit sync for eight requests (encrypted streams also sync their
preamble), rollback and later progress, bounded queue
rejection and close/drain ordering, partial writes followed by I/O errors and failed/ambiguous sync, plain
and encrypted durable-image recovery, process death around fsync/acknowledgement,
concurrent indexed IPC writes, truncated IPC requests, and a durable IPC write
whose acknowledgement is lost when the host dies. FileStream subclasses
inject faults; simulated power loss restores only the last successful sync image.
Actual process-kill tests retain the OS cache and are a different fault model.
These are Linux/ext4 research results, not Windows/macOS validation or a claim
about devices that lie about successful fsync. Do not treat the proof branch as a
production-ready library feature.
