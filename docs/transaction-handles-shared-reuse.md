# Shared transaction holder and wrapper reuse

The measurements and local counts below describe the original reuse integration
through `560529066`. Subsequent review fixes and the merged upstream safety gates
are recorded in [the integrated safety review](transaction-handles-safety-review.md);
these historical numbers do not validate the later implementation.

This is the narrowly scoped integration of `experiment/pr133-shared-child` into
PR #133. The comparison baseline is PR head
`c8c0cfab623a22880b1d71eb966b09e89f9153b9`; the measured production implementation is
`8fb87fa123a88ef143e3fdfa1bae564032f9a941`. Validation/report commits through `560529066` do not change
that production source. The earlier experiment's measurements are historical;
the results below come from this implementation.

## Ownership and retained resources

A live `SharedEngine` keeps at most one idle transaction child wrapper. A successful
holder closes the underlying `LiteEngine`, releases native writer ownership, ends
its admission accounting, closes idle data/log handles and releases the child's
coordination participation and native mode-admission lease, then returns the wrapper
under the parent's `_useLock`.
The next handle checks it out only after the existing local admission gate and
reopens a new core after acquiring native ownership. No page cache, transaction,
WAL view or coherent storage core crosses that release/acquire boundary.

An open/close cleanup failure discards the wrapper. Checkout also discards it when
its password or serialized collation differs from the current detached settings
snapshot. This preserves public `Rebuild` password/collation changes and mutable
serialized policy on a derived collation. Parent disposal sets `_disposed`
before draining its connection, refuses further cache publication, and disposes any
cached wrapper even if connection cleanup throws. Parent admission disposal remains
in a `finally`. The wrapper retains settings, mutex-owner machinery and the
reader-registry wrapper,
not writer ownership, idle data/log handles, mapped coordination participation or
its native mode lease. Coordination demand resets to the original one-operation
child policy; reuse alone must not create a persistent mapped participant.
Ordinary operations on the parent still own their independent Shared lifetime
admission. A facade that has used only handles can still hand off to a Direct
process between handles, as the previous PR head allowed.

Holder jobs use a separate pool from session-close cleanup. At most two idle threads
remain for one second; busy threads do not cap progress on unrelated databases.
Each job restores its execution context and test stream probe, and returns its whole
callback frame before the worker becomes idle. A holder's cache-owner link is weak;
settings snapshots and read-policy references retain the existing abandonment
protections. Completion waits for the job, rather than joining the reusable thread.

The transaction API, admission budget/cancellation, close/drain policy, native lock
release/acquire pair, and WAL confirmation/device-sync path are unchanged. This
change includes no group commit, IPC writer service, storage-core cache, native
writer retention, atomic session counter, single-bound-scope or inline-close change.

## Safety evidence

| Invariant | Evidence |
| --- | --- |
| Actual holder and wrapper reuse with a fresh core, external writer progress and refreshed indexed reads | `TransactionHandleChildReuse_Tests`: plain/encrypted; identical worker/wrapper, distinct cores, three external-process commits, exact 64-row payload/ID model, secondary-index results, unrelated collection, rollback and two cold reopens |
| Timeout/cancellation after checking out a cached wrapper preserves another process's owner and allows retry | `TransactionHandleChildReuse_Tests`: plain/encrypted, process handshake, native-wait observation, original cancellation token, failed-child discard and retry |
| Failed reused open/core close releases ownership; known commit remains committed | `TransactionHandleChildLifetime_Tests`: injected open/close errors, same exception identity, cache discard, successful subsequent write and two indexed cold reopens |
| WAL write failure remains indeterminate and does not leak unconfirmed rows through wrapper reuse | `TransactionHandleChildLifetime_Tests`: plain/encrypted write injection, fresh-core retry, committed model/index/sentinel checks and two cold reopens |
| Completed handles and idle/active workers do not root abandoned sessions/application state | `TransactionHandleChildLifetime_Tests`: retained completed handle, read-policy closure/AsyncLocal graph, cached or active child, graph collection while worker remains idle, abandoned multi-page rollback and indexed cold reopen |
| Child reuse preserves mode-admission handoff | `TransactionHandleChildReuse_Tests`: plain/encrypted Direct process writes between handles of a handle-only facade, same cached wrapper reads the update, repeated cold reopen; standalone baseline/pre-fix/fixed admission comparison |
| Reuse cannot hide changed effective settings | `TransactionHandleChildSettings_Tests`: encryption enable/change/removal, rebuilt collation, mutable serialized collation policy, subsequent handle writes and two indexed cold reopens |
| Pool handoff cannot retain callbacks/contexts or starve other owners | `TransactionHandleHolderScheduler_Tests`: same worker, normal/suppressed execution contexts, blocked workers, idle graph collection, expiry/dispatch interleaving |
| Process death during a handle opened after a warm handle cannot publish unconfirmed WAL | Added plain/encrypted `TransactionHandleProcess_Tests` cases with native-exclusion control and repeated committed/index/sentinel recovery checks |

The existing handle, Shared, lifetime, abandonment, admission, process-death and
failure regression tests are retained. The runs use Release with
`TestingEnabled=true`, `tests.runsettings` and its 300-second session limit. Child
processes pin the exact runtime and architecture of the test host. Local results
are Linux x64 evidence; hosted platform coverage is identified separately in PR #133.
The affected storage protocol is unchanged. Tests exercise injected I/O/cleanup
failures and process termination, not physical power removal on every device.

Local results on the final production implementation above:

| Partition | net8.0 | net10.0 |
| --- | ---: | ---: |
| Handle/completion/abandonment | 143 passed | 143 passed |
| Shared-engine and cross-process Shared | 383 passed | 383 passed |
| Native admission | 200 passed | 200 passed |
| Remaining Shared/process cases | 240 passed | 240 passed |
| **Distinct passing cases** | **966** | **966** |

No failing or skipped cases occurred in the final partitions. A separate negative
run against the pre-invalidation implementation failed all five new settings cases:
rebuild left stale credentials/collation, and a changed serialized policy was ignored.
All five pass with invalidation. A second baseline comparison found that retaining
the child's mode lease refused Direct admission that the previous PR head allowed
between handles; releasing that lease restores the baseline behavior. These are
correctness constraints on wrapper reuse, not additional performance experiments.
The unchanged settings-abandonment regressions also caught releasing native admission
before coordination participation closed; the final implementation preserves that
cleanup order. Final passing results include those same regressions without weakening
their assertions. The archive retains that before/after evidence and
the superseded validation/partial timings; they are not final performance samples. Production library builds
pass for netstandard2.0/net8.0/net10.0; test projects compile for net462/net481.
Framework execution was not performed locally. Build logs retain existing warnings.

## Measurement method

Both libraries are production Release/net10.0, `TestingEnabled=false`, built in
separate worktrees from test-hook builds. The exact same `TransactionHandleBenchmarks`
runner executes `steady shared handle read` with 0, 1 or 10 reads per handle. Zero
still performs begin/GetCollection/commit. Each process keeps its facade alive and
uses durable commits; this read-only workload measures transaction setup/teardown,
not durable-write throughput.

Four fresh processes per build/case alternate baseline/candidate then candidate/
baseline: 24 processes total, five seconds warmup and five one-second measured
windows each. `DOTNET_TieredCompilation=0`, .NET 10.0.11, Ubuntu 24.04 x64/ext4.
No local builds/tests/profiles overlap measurement. Each run validates returned
values, indexed results and an unrelated sentinel, then verifies cold reopen.
All windows/processes are retained. Allocation uses process-wide
`GC.GetTotalAllocatedBytes(true)` and includes holder-thread work. Rates are medians
of per-process aggregate transaction rates; speedups are medians of paired ratios.
The host has uncontrolled unrelated load, so these are descriptive local results.

## Original integration measurements

| Reads/handle | Head tx/s | Reuse tx/s | Paired speedup | Head bytes/tx | Reuse bytes/tx |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0 | 1,449 | 1,897 | 1.311× | 227,782 | 220,778 |
| 1 | 1,298 | 1,679 | 1.303× | 237,902 | 230,905 |
| 10 | 1,103 | 1,411 | 1.279× | 302,146 | 295,222 |

The one-read improvement is **30.3% paired throughput** with **2.9% fewer allocated
bytes**. Per-process throughput ranges were 1,278–1,318 tx/s before and
1,668–1,723 tx/s after. All 24 processes and all 120 measured windows are retained;
every run passed its result and cold-reopen checks.

The historical approximately 4.69× / 155 KB one-read result was **not reproduced**
on this semantics-preserving implementation. The final version releases mode
admission and coordination participation between handles and preserves changed
settings. Holder/wrapper reuse improves throughput, while the required core and
admission lifecycle costs remain. This result does not claim to eliminate the full
Shared-handle penalty or establish durable-write throughput.

| Production DLL | SHA-256 |
| --- | --- |
| Baseline | `dc319b9c8f7774ede2898ae315c7b339a880ddaa282e362491e9cd18834373da` |
| Final reuse | `856f8a4e1980562051a394ab621beecbcaeb5fb7014b12a110ca3dda9e780842` |

[Raw windows, every paired ratio, latency/range summaries, exact production/test binaries,
regression evidence and replay scripts](https://github.com/litedb-org/LiteDB-Artifacts/tree/9e091b3ec228006000ac776867fcac6d1c9acbc2/pull-requests/JKamsker-LiteDB-133/2026-09-30-shared-reuse).
The archive also retains the interrupted preliminary run and expected pre-fix
failures; those are separated from final safety and performance evidence.
