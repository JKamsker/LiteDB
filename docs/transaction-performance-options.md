# Transaction performance options for PR #133

This report compares upstream dev, the stacked parent and PR #133, then evaluates
independent ownership and small-write strategies. The current PR's implementation
is unchanged. Each library variant is on a separate proof branch; the controller
and measurement tools are on [t3code/benchmark-group-commit-experiments](https://github.com/JKamsker/LiteDB/tree/t3code/benchmark-group-commit-experiments).

## Baselines and method

| Build | Revision |
| --- | --- |
| origin/dev | `5dd942a7367c361fadd600be4ce10aace2768b27` |
| Parent / #132 | `49c327cf1926fa300f9eb7477eb4bcb404f75c43` |
| PR #133 | `c8c0cfab623a22880b1d71eb966b09e89f9153b9` |

Production Release/net10.0, `TestingEnabled=false`, .NET 10.0.11, Ubuntu 24.04
x64, ext4. Separate worktrees isolate test-hook and production builds. No local
builds, tests or profiles ran during timed comparisons. This is a shared host;
unrelated load and storage variability are uncontrolled. These are descriptive
local results, not confidence bounds or predictions for other hardware.

Read/attach runs use four fresh processes per revision/case, alternating build
order, five seconds warmup and five one-second active windows. The primary matrix
has tiering disabled. All 60 upstream-baseline processes check read results,
indexed results and an unrelated sentinel after cold reopen. The dev comparison
includes both stacked PRs; the parent comparison isolates #133. Percent changes
are medians of paired ratios, not ratios of the displayed median rates.

| Workload | dev ops/s | Parent ops/s | Head ops/s | Head/dev | Head/parent |
| --- | ---: | ---: | ---: | ---: | ---: |
| Direct ordinary read | 94,030 | 85,813 | 80,138 | -14.8% | -6.7% |
| Direct legacy begin/read/commit | 91,424 | 86,772 | 73,153 | -19.7% | -13.7% |
| Direct attach/count/dispose | 7,008 | 16,862 | 11,298 | +59.2% | -33.3% |
| Shared ordinary read | 51,511 | 52,797 | 46,522 | -11.8% | -11.9% |
| Shared legacy begin/read/commit | 8,293 | 7,875 | 7,906 | -5.4% | -0.2% |

The parent-only attach comparison hides #132's benefit: pooling avoids reopening
the whole Direct engine for each attached facade. Median allocation per attach
falls from 168,125 bytes on dev to 29,208 on the parent, then 29,920 on the head.
Ordinary Direct reads instead allocate 10,112 / 10,264 / 10,248 bytes. Their
regression is not explained by increased allocation volume alone. Source paths
add session lifetime guards, operation leases, context selection and reader
ownership; isolated variants below test parts of that cost without assigning
the entire regression to any one lock.

## Independently measured ownership options

Each variant starts at the same PR head; gains must not be added together.

| Option | Proof branch | Scope and acceptance |
| --- | --- | --- |
| Atomic session admission | [experiment/pr133-session-refcount](https://github.com/JKamsker/LiteDB/tree/experiment/pr133-session-refcount) | CAS combines closed admission and active operation count; thread-local active scopes support self-close rejection. Close/drain remains independently scheduled. |
| One bound context scope | [experiment/pr133-single-scope](https://github.com/JKamsker/LiteDB/tree/experiment/pr133-single-scope) | Bypasses the redundant ordinary adapter for internal transaction engines; retains public reentry checks and one-use engine authorization. |
| Reuse Shared holder threads | [experiment/pr133-shared-worker](https://github.com/JKamsker/LiteDB/tree/experiment/pr133-shared-worker) | Separate pool of at most two empty idle workers, one-second expiry. Native ownership releases after every transaction; child/core still reopen. |
| Reuse holder plus child wrapper | [experiment/pr133-shared-child](https://github.com/JKamsker/LiteDB/tree/experiment/pr133-shared-child) | Also caches one detached child SharedEngine per originating SharedEngine. Storage core still reopens after each native handoff. |
| Inline idle close | [experiment/pr133-inline-close](https://github.com/JKamsker/LiteDB/tree/experiment/pr133-inline-close) | **Rejected diagnostic upper bound.** Zero active calls/handles does not prove final release cannot block. |

The common Shared gate remains per database. Empty workers are pooled; the child
wrapper cache is per originating session, not a universal process-wide engine
cache. It retains one additional Shared participant while that session lives and
releases it on session close. Tests verify that another process can acquire native
writer ownership between uses and that the next handle sees its writes, with plain
and encrypted files. Idle pool workers retain no application owner, child engine,
or admission. Their resource cost is up to two additional idle threads for one
second, alongside the pre-existing cleanup pool.

Retaining the actual coherent storage core across independent process writes is
not implemented here. It requires a generation/cache invalidation and recovery
protocol, plus replacement/checkpoint/snapshot tests. Retaining a stale core while
releasing its native lock would be unsafe. The wrapper experiment isolates the
smaller reuse step; its performance must not be presented as the payoff from full
core reuse.

Dispatch inspection corrected one hypothesis: its nested `Session.Enter()` is
an `EngineContext` thread-static scope, not a second `SessionLifetime` monitor.
Removing only that scope fails because the intermediate ordinary adapter clears
the binding. The tested variant removes that redundant adapter only for the
internal TransactionEngine and checks that the outer scopes are still installed.
Ordinary database calls continue to clear ambient handle bindings. It preserves
the consumed dispatch ticket that prevents raw-engine callbacks from enlisting.

The naive inline-close variant fails
`Idle_final_release_is_bounded_and_does_not_block_other_sessions`: a 100ms close
budget instead waits for the injected ten-second blocked release. It must not be
merged. A safe future detach optimization needs a provably nonblocking release
path with the non-final host reference reserved atomically; observing idle state
or a reference count without such a reservation is insufficient.

Separate diagnostic traces recorded 6,216 CLR `Thread/Creating` events for the
head and 9 each for holder and holder-plus-wrapper reuse, over approximately
6.3–6.5 seconds including startup and warmup. These are creation events, not live
thread counts, and instrumented trace timings are excluded from throughput.

The atomic-admission variant measured **+1.3% Direct ordinary reads**, **+2.3%
Direct legacy reads**, and **−1.4% Shared ordinary reads** on median paired
throughput. Its four Direct ordinary ratios were 1.0114, 1.0145, 1.0008 and
1.0502; Shared ratios were 0.9730, 1.0942, 0.9898 and 0.9821. This mixed, small
result does not support prioritizing it as the main regression fix.

Removing the redundant bound adapter/context scope measured **+1.7% for Direct
handles** and **+0.9% for Shared handles**. The Direct paired ratios were
1.0244, 1.0189, 1.0140 and 1.0148; Shared ratios were 1.0154, 1.0118, 0.9903
and 1.0059. These scoped changes pass the safety regressions, but are incremental
improvements rather than a recovery of the full overhead.

The strongest measured option is the cached Shared child wrapper. At zero reads,
paired throughput is **6.04×** the head, versus **1.29×** for thread reuse alone.
At one read, those ratios are **4.69×** and **1.26×**. Median one-read allocation
is approximately 237,902 bytes on the head, 233,774 with thread reuse and 155,068
with wrapper reuse. Thus most of this observed benefit comes from keeping the
Shared participant/wrapper, beyond avoiding thread creation; individual allocation
pooling was not needed to obtain that reduction.

| Option / workload | Head ops/s | Variant ops/s [min–max] | Paired change | Variant bytes/op |
| --- | ---: | ---: | ---: | ---: |
| refcount: direct-ordinary-read-1 | 81,839 | 83,701 [82,221–83,894] | +1.3% | 10,248 |
| refcount: direct-legacy-read-1 | 75,355 | 77,916 [76,034–79,113] | +2.3% | 10,128 |
| refcount: shared-ordinary-read-1 | 48,184 | 48,206 [46,748–50,157] | -1.4% | 10,552 |
| scope: direct-handle-read-1 | 65,525 | 66,756 [66,427–67,874] | +1.7% | 11,488 |
| scope: shared-handle-read-1 | 1,294 | 1,304 [1,296–1,314] | +0.9% | 237,881 |
| worker: shared-handle-read-0 | 1,443 | 1,869 [1,785–1,894] | +29.1% | 223,638 |
| child: shared-handle-read-0 | 1,443 | 8,746 [8,482–9,225] | +504.0% | 144,876 |
| worker: shared-handle-read-1 | 1,302 | 1,634 [1,615–1,647] | +25.5% | 233,774 |
| child: shared-handle-read-1 | 1,302 | 6,064 [6,020–6,225] | +368.8% | 155,068 |
| worker: shared-handle-read-10 | 1,113 | 1,357 [1,345–1,399] | +22.0% | 298,082 |
| child: shared-handle-read-10 | 1,113 | 3,924 [3,757–4,114] | +250.6% | 219,364 |
| inline: direct-ordinary-open-1 | 11,017 | 15,627 [15,467–15,870] | +41.3% | 29,824 |

## Small durable writes and the IPC controller

[Tool, contract and replay commands](../tools/WriteControllerExperiments/README.md).
All strategies insert immutable commands with a 64-character payload into one
collection with a secondary index. Each caller has one outstanding request.
Direct shares one database instance; Shared has one session per caller. Both
IPC strategies use a genuinely separate server process and persistent client
connections. The existing experimental CoordinatedEngine is a control.

This insert workload differs from the older PR report's updates to existing rows;
compare strategies within this matrix, not by mixing its absolute rates with the
older write table.

The new bounded queue serializes accepted commands on one dedicated writer.
Batch size one isolates scheduling/ownership from batching. Batch 32 drains
already-queued commands, optionally waiting up to one millisecond to form a group.
Every mode retains durable commits and the default checkpoint policy. Reported
latency includes queueing, IPC and commit; shutdown/drain is separate. Each run
has three seconds warmup, five seconds measurement, then two cold reopens checking
every ID, full payload, indexed result and an independent per-caller record model.

**Batching coalesces commands into one atomic transaction. It is not independent
open transactions sharing a single fsync.** A duplicate rolls back its whole group;
the application must accept that grouping. No success is sent before the durable
commit returns. Failure during commit stops the controller and reports an unknown
outcome. Queue-full and closed-controller responses mean no command was accepted.
Loss of an IPC reply after a complete request is indeterminate; there is no
automatic retry, deduplication or durable inbox. Reconcile IDs before retrying.

The IPC controller retains exclusive Direct admission for its lifetime. Other
processes must route through it; they cannot open ordinary Direct/Shared readers
alongside it. This proof exposes only writes. A production multiprocess service
would need a read/snapshot API or integration with the existing coordinator,
whose existing read behavior this independent prototype does not implement.

The existing collection/header locks cover commit and durable flush. Independent
same-collection transactions cannot all reach a commit queue while one owns that
lock. A true engine-level group commit would need to change that locking and
publication protocol and handle uncertain outcomes for several WAL confirmations.
The command queue deliberately measures the option that amortizes fsync for this
workload while reusing the current engine's atomic commit and recovery protocol.

This is a time-based growing-dataset workload: faster strategies insert more
rows before measurement ends. Shared's default close checkpoint threshold also
produces a different WAL/checkpoint pattern from a retained Direct/coordinator
engine. The end-to-end comparison therefore does not isolate a mutex instruction
or fsync alone.

Client allocation counters include harness overhead and exclude a separate IPC
server, so do not compare those columns as total-system allocation. Client/server
CPU is recorded separately. Peak WAL is sampled every 25ms and is a lower bound;
it is not total write amplification. IPC commit totals include warmup; in-process
queue commit deltas cover measured work only. Controller startup is not timed.

### Durable writes against origin/dev

| Callers / API | dev writes/s | Head writes/s | Paired change |
| --- | ---: | ---: | ---: |
| 1 / direct | 186.0 | 187.6 | +0.8% |
| 1 / shared | 117.4 | 118.3 | +1.0% |
| 16 / direct | 168.5 | 164.6 | -2.8% |
| 16 / shared | 114.7 | 113.8 | -1.1% |

Single-caller writes do not show a meaningful regression in this environment.
Sixteen-caller writes show a small throughput penalty, substantially below the
read-path regression. These synced file-backed writes are dominated by storage
and coordination; this does not disprove the CPU overhead observed in reads.

### Controller strategies on the PR head

| Strategy | 1 caller writes/s | 16 callers writes/s [min–max] | 16-caller p99 ms | Paired vs Direct |
| --- | ---: | ---: | ---: | ---: |
| direct | 187.0 | 164.0 [162.4–165.4] | 394.3 | 1.00× |
| shared | 116.5 | 113.9 [112.7–114.4] | 168.0 | 0.69× |
| existing | 175.4 | 154.1 [151.6–155.2] | 485.9 | 0.94× |
| queue, batch 1, 0ms linger | 186.0 | 184.0 [183.7–185.2] | 118.3 | 1.12× |
| queue, batch 32, 0ms linger | 186.7 | 2,008.1 [1,997.0–2,014.8] | 40.5 | 12.22× |
| queue, batch 32, 1ms linger | 154.6 | 2,152.9 [2,117.3–2,163.5] | 39.4 | 13.16× |
| ipc, batch 1, 0ms linger | 180.6 | 182.7 [177.1–184.0] | 120.7 | 1.11× |
| ipc, batch 32, 1ms linger | 150.4 | 2,078.4 [2,066.9–2,084.7] | 37.2 | 12.64× |

At sixteen callers, serialization without grouping modestly improves throughput
and lowers p99 latency. Coalescing requests into shared transactions produces the
large gain: roughly 12–13× Direct throughput here. The IPC hop preserves most of
that batching gain. This does not demonstrate that IPC itself makes storage faster.

With one caller, one millisecond of linger costs roughly 18–20% throughput. A
zero-linger queue avoids that deliberate wait and still groups already-pending
requests under contention. Larger groups couple failure outcomes and the bounded
queue can reject excess load; neither option is a transparent transaction adapter.

At sixteen callers, sampled peak WAL is about 13.4 MiB for contended Direct,
13.1 MiB for the existing coordinator, 8 MiB for queued/IPC variants and under
0.5 MiB for Shared. These measurements include different default checkpoint/close
behavior and in-flight transactions; they are not total write amplification or a
general WAL-space guarantee. Full client allocation, client/server CPU, latency and drain ranges
are in the raw output and summary.

## Safety evidence and boundaries

The controller changes no library storage implementation or on-disk format.
Seventeen focused scenarios on the production head establish no early success,
one confirmed-WAL sync for eight queued commands, complete batch rollback,
continuation after duplicate rejection, queue capacity and graceful close,
partial writes, failed sync, sync followed by failure, plain/encrypted cold
recovery, actual process kills before/after sync and after acknowledgement,
concurrent indexed IPC writes, incomplete request frames, and a durable IPC write
whose acknowledgement is lost. Encrypted streams additionally sync their preamble;
the probes distinguish that setup sync from the one shared commit sync.

The simulated power-loss model restores only the last successfully synced WAL
image with the checkpointed data image. It checks exact prior or complete new
group contents and secondary indexes on repeated recovery. Process-kill tests
retain the OS cache; they are not represented as power-loss tests. Fault-injection
streams deliberately throw at the confirmed-WAL flush or after partial writes.
These assumptions do not cover storage devices that falsely acknowledge sync,
arbitrary hardware corruption or network filesystems.

The unmodified head also passes 102 relevant WAL durability, power-loss,
failure-cleanup and handle failure/process regressions on net10.0. All four viable
library variants pass their relevant handle/ownership/abandonment regressions on
net8.0 and net10.0. Worker-pool tests additionally force reuse, expiry/handoff,
blocked-peer progress, context isolation and graph collection. The child-wrapper
variant has additional external-writer/cold-reopen coverage and broader Shared
regressions. Exact test counts and revision/configuration are retained with the
evidence. The inline-close failure is the reason that option is rejected, not an
unresolved acceptance claim.

| Proof revision | net8.0 | net10.0 |
| --- | ---: | ---: |
| Single scope `70f517e7f` | 116 passed | 116 passed |
| Atomic admission `fb14813d2` | 116 passed | 149 passed |
| Holder threads `5c747f0fc` | 120 passed | 120 passed |
| Holder + child `56f7cf548` | 122 passed | 120 handle + 82 Shared tests passed |
| Inline close `3c4ca96d4` | Not pursued | Rejected: bounded-close regression failed |

All five production library variants also build for netstandard2.0, net8.0 and
net10.0. These local test results are separate from the existing PR-head hosted
CI. No passing hosted CI is claimed for the proof branches.

This is bounded Linux research evidence. Windows/macOS runtime/file-locking
validation, a production public controller API, durable retry protocol, service
takeover, hostile-client limits and coherent-core reuse remain outside these
proofs. The branches are options for review, not merge approval. Existing PR #133
read/attach/Shared-handle tradeoffs still require explicit maintainer acceptance.

## Reproduction and raw evidence

[Published evidence](https://github.com/litedb-org/LiteDB-Artifacts/tree/2d58276b767d82d01d894b3af6bceb5048f47bd1/pull-requests/JKamsker-LiteDB-133/2026-09-30-options) contains all **240 uninstrumented fresh-process
runs** (60 upstream comparisons, 84 ownership comparisons and 96 durable-write
runs), every paired ratio, raw TRX results, the 17-scenario controller logs,
separate diagnostic traces and SHA-256 identities. `production-runners.zip`
retains the exact measured libraries and runner assemblies. No measured samples
were discarded. Each run's manifest records its command, configuration and exit.
The initial failed scope/worker prototypes were corrected before timing; their
final safety results are listed above. Inline close remains explicitly rejected.

The controller C# measured source is `24c8e88dc`; subsequent changes only document
results, add summarization and make project source inclusion explicit for isolated
build directories. The final project rebuild and all 17 controller safety scenarios
pass again. Summarize the packed evidence with
`scripts/summarize-transaction-handle-steady.py` or
`scripts/summarize-write-controller.py`. Rebuild each isolated library branch with
`TestingEnabled=false`, supply its DLL through `LibraryPath`, and run the commands
from the respective manifests after replacing machine-specific absolute paths.

