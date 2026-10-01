# Transaction handle validation

The original measurements below predate holder/wrapper reuse. See
[Shared holder reuse validation](transaction-handles-shared-reuse.md) for the
current Shared-handle implementation, new safety coverage and fresh comparison
against the previous PR head.

This report covers the handle/lifetime changes layered on PR132. The parent is
`49c327cf1926fa300f9eb7477eb4bcb404f75c43`; production changes measured below end at
`d3ddd94f2279facba4107487b1da2c7f8ccf1b0f`. Consult PR133 for the final tested head and
hosted check results. This is finite evidence for the stated fault model, not a
claim that every device, scheduling interleaving or failure has been proved safe.

## Ownership invariants and discriminating coverage

| Invariant | Main evidence |
| --- | --- |
| Handle identity survives creator exit and sequential handoff; ordinary/legacy calls remain independent | `TransactionHandle_Tests`, `TransactionHandleBinding_Tests`, `TransactionHandleRawEngine_Tests` |
| Public mapping, callbacks, enumeration and completion share one overlap guard; internal composition remains usable | `TransactionHandleBinding_Tests`, `TransactionHandleInclude_Tests`, raw callback refusal/terminal-state regressions |
| Separate same-thread transactions do not recurse into the same collection lock | `TransactionHandleModel_Tests.Same_thread_independent_handles_cannot_recurse_into_the_same_collection_write_lock` |
| Bound cursors cannot escape completion; retained terminal objects release storage ownership | `TransactionHandle_Tests`, `TransactionHandleLifetime_Tests`, `TransactionHandleAbandonment_Tests` |
| Close timeout retains executing dependencies, rejects new work and completes without another Dispose | `TransactionHandleLifetime_Tests`, `TransactionHandleCloseCleanup_Tests`; pending-begin cancellation and holder callback reentry |
| Maintenance/fatal teardown waits through operation cleanup | Updated `Issue2965_Tests`, `FuzzingContract_Tests`, fatal/close cleanup tests |
| Known commit remains committed after cleanup error; uncertain WAL failures are terminal without false rollback | `TransactionHandleFailure_Tests`, `TransactionHandleSharedCleanup_Tests` |
| Process death releases exclusion and recovery preserves acknowledged data | `TransactionHandleProcess_Tests`: plain/encrypted, Direct/Shared, unconfirmed writes and acknowledged nonempty WAL, repeated cold reopen |
| Recovered records and indexed results match an independent committed-state model | `TransactionHandleModel_Tests`: seeds 1323064/30673041, both backends, 12 rounds of 12 insert/update/delete operations with thread handoff and commit/rollback/disposal |
| Read-only policy survives attachment to a writable host; unsupported capabilities fail before side effects | Binding/model tests, Shared policy/private-storage tests, precompiled custom-provider fixture |
| Shared child engines preserve degradation diagnostics but later commits retry device sync | `TransactionHandleSharedPolicy_Tests`: plain/encrypted injection specifically between confirmed-WAL flush phase hooks |
| Callback-enabled Shared handles need only their lifetime holder, including foreign-thread completion | `TransactionHandleSharedPolicy_Tests`: 20 consecutive handles, no auxiliary mutex-holder thread |
| Whole-cache abandonment differs from leaking an in-use frame of a surviving cache | `PageBufferAbandonment_Tests`, young/promoted handle-abandonment tests, independent exact-parent legacy reproduction |
| One bounded Shared admission budget covers local and native waiting; failures preserve the current owner | `TransactionHandleAdmission_Tests`, remote-owner timeout/cancellation in `TransactionHandleProcess_Tests` |
| Begin cancellation detaches callbacks; later cancellation cannot affect a returned handle/commit | `TransactionHandleAdmissionLifetime_Tests`, foreign-thread commit after token cancellation, callback/AsyncLocal abandonment and cold reopen |
| Holder configuration cannot root application fields on derived settings/collation | `TransactionHandleSettingsLifetime_Tests`: both public construction paths fail before the fix; encrypted multi-page abandonment, indexed committed sentinel, repeated cold reopen and Shared reacquisition pass after it |
| Reused close workers release each job/context and cannot starve another session | `TransactionHandleCleanupScheduler_Tests`: actual worker reuse, blocked peers, context restoration, graph collection, expiry/dispatch race and file-backed rollback |
| Advancing a reader holds an operation lease through callbacks | `TransactionHandleReaderOperation_Tests`: blocked second-row callback prevents exclusive teardown even when cursor dependencies are ignored |
| Close cleanup progresses when all application pool workers are blocked disposing sessions | Isolated process restricted to one pool worker: idle/active Direct/Shared disposal and cold data/index/sentinel checks; concurrent retry and blocked-release tests |
| Suppressing holder execution-context flow preserves default/persisted collation and cannot silently change an impersonated Windows caller's identity | Cold-process Turkish collation/index test; Windows/CLR impersonation rejection and normal retry in `TransactionHandleContext_Tests` |

Focused local runs cover 137 tests on each of net8.0/net10.0, including the
transaction-handle, posted-release, abandonment, fatal-cleanup and FuzzingContract
cases, with the final collation snapshot included. All target builds pass, including
net462/net481.
Earlier broader local partitions
passed 352 Shared and 1,568 issue tests, with one existing issue skip. Final hosted
coverage, including Framework runtime execution, is reported separately in the PR.

The persisted protocol is unchanged: these tests exercise owner/lifetime changes
at existing WAL/flush/recovery boundaries. They include failed writes, failed
flushes, failed cleanup, process termination, cold reopen, index/document agreement
and unaffected sentinel collections. They do not simulate physical power removal
on every supported device. Existing WAL/power-loss/fuzz suites remain part of CI;
native filesystem and durability requirements remain those documented by PR132.

## Compatibility and known-bad execution

[`test-transaction-handle-compatibility.py`](../scripts/test-transaction-handle-compatibility.py)
compiles the old interface implementations and consumer against the actual parent,
then runs that same consumer binary with the candidate. It also executes an old-API
bulk-input failure followed by injected rollback-cleanup failure: parent exits 17
because cleanup masks the original error; candidate exits 0 with the original error
and attached cleanup diagnostic. This is a behavioral failure, not an old compiler
rejecting a new API. The fixture validates capability refusal and caller-owned
engine disposal. CS0618 fails a warnings-as-errors consumer; a targeted
`WarningsNotAsErrors=CS0618` exception builds successfully.

Test-hook DLL SHA-256 values for that run:

| Build | SHA-256 |
| --- | --- |
| Parent | `345823b9a6b8a7de070182ac5d086c417668f74f0c624312a9f1b14fad33ae89` |
| Candidate | `5a7fc40da4f416b8631f1c55ffefb36c43ed030b944ca62d80eac2aede41eeb0` |

The original DEBUG/TESTING dirty-page finalizer assertion was also reproduced on
that exact parent using only legacy BeginTrans. Observer-mode recovery preserved
committed records/indexes/sentinel across two cold reopens. The diagnostic change
uses a short weak cache reference to distinguish an unreachable cache graph;
explicit cache disposal and live-cache leak diagnostics remain strict. Finalizers
perform no new rollback or storage I/O.

Independent aspect reviews prompted fixes for callback binding, indexer reentry,
cleanup-error masking, indirect holder/session-close reentry, callback ownership
cycles and Shared child policy propagation. Reviewers rechecked their retained
findings. These were bounded source/reproduction reviews, not universal approval
of untested behavior.

The admission review additionally found an idle holder stack temporary retaining a
disposed linked cancellation source, whose callbacks captured the facade. A heap
root trace confirmed that chain. Acquisition now ends on a separate stack frame,
and admission disposal clears its token/source references. Callback and AsyncLocal
abandonment cases verify collection, native release and preservation of committed
sentinel data without the uncommitted row. Windows impersonation is an explicit
capability refusal before queuing; the holder never silently switches identities.

Hosted tests also exposed thread-pool starvation in session close. The original
close queued its cleanup to the pool while blocking the disposing workers. A
separate close worker now progresses independently; a failed worker startup retains
Closing ownership and can be retried. Concurrent retries release exactly once.
The close deadline still covers blocked cleanup, including zero-handle sessions.

Two further public roots were reproduced: application fields on a derived
`EngineSettings`, and on an explicitly supplied `Collation`. Before the fix both
abandonment cases retained the database, handle and application state. The holder
now snapshots the base settings and serialized collation policy; tests also verify
overridden collation serialization and unset/explicit transaction-limit semantics.
The caller-facing settings and ordinary Shared behavior remain intact.

Cleanup-worker reuse retains at most two idle workers for one second, without a
session or caller execution context. Active workers are not capped. Forced expiry,
blocked-peer and context-retention tests preserve the independence requirement.

## Performance and resource costs

The final production assembly is `d3ddd94f2279facba4107487b1da2c7f8ccf1b0f`,
compared with parent `49c327cf1926fa300f9eb7477eb4bcb404f75c43`. Both are isolated
Release/net10.0 builds with `TestingEnabled=false`, .NET 10.0.11, SDK 10.0.400,
Ubuntu 24.04 x64 and local ext4 temporary storage. The host is a Ryzen 9 3900X
(12 cores/24 logical processors), with no affinity pinning. No local builds or
tests ran concurrently with timing. Unrelated load on this shared development host
was not controlled; all samples, including slow ones, are retained.

The final comparison uses **52 fresh processes**, two pairs per scenario in AB/BA
order, each with 10 seconds warmup and ten one-second measurement windows. The
main comparison disables tiered compilation; a separate default-tiering check is
below. Windows use active measurement time, excluding reporting gaps. Every run
checks the expected read value and cold-reopened records, index and sentinel.
These two pairs are descriptive evidence, not precise confidence estimates;
windows within a process are not independent repetitions. This measures warm work,
not cold startup. The cases use ordinary settings, not derived settings or custom
collation serialization. All measured latencies fit the sampling buffer. Default-tiering runs do not show
a consistent upward warmup trend; first-versus-last three-window means vary from
about −11% to +6%, so within-run drift remains a measurement limit.

Rates below are operations/transactions per second, shown as median [process
min–max]. Changes use the median **paired** ratio, not a ratio of the displayed
medians. Every individual paired ratio is shown. Percentiles are medians of window
percentiles, not percentiles of a pooled latency distribution.

### Profiling and focused improvements

Separate historical diagnostic traces at `fe4d1725e` located session/operation
guards and reader advancement in sampled Direct stacks. They also exposed repeated
thread creation in close scheduling. Sampled thread time includes waits and
inclusive frames overlap; it does not quantify an individual guard's CPU cost.

The four-file guard/reader bundle (`fe4d1725e` → `56f7a249e`) avoids an operation
lease for an empty abandoned-context queue and cached reader returns, removes a
redundant open-session cleanup check, and narrows condition notifications without
removing predicate-changing notifications. Two paired attribution processes per
version/scenario measured Direct ordinary-read gains of **9.8% and 11.3%**, and
legacy-read gains of **8.2% and 9.6%**. This attributes the bundle, not its members.

Cleanup-worker reuse (`56f7a249e` → `e3f328f9b`) measured attach gains of **106.9%
and 97.4%** against the per-close-thread correctness fix. Independent diagnostic
traces recorded **47,316 CLR Thread/Creating events in 10.545 seconds before reuse,
and 1 in 10.561 seconds afterward**. Those counts include startup/warmup and are
neither concurrent OS-thread counts nor a performance multiplier. Reuse keeps at
most two idle workers for one second; blocked workers do not cap other sessions.
Context-restoration, graph-collection, expiry/dispatch and rollback tests protect
that optimization.

A separate allocation-only trace at `fe4d1725e` sampled predominantly byte-array
and string allocations in Shared handle work. These are historical sampling
estimates grouped by triggering type, including setup/warmup/reporting; they are
not exact object totals, retained memory or final per-operation allocations. The
mixed diagnostic profile captured no allocation ticks and is not allocation
evidence. Final allocation totals below come from uninstrumented counters and
include helper-thread allocations. Profiles and timings are separate experiments.

[Attribution raw runs and source identities](validation/transaction-handles/attribution/manifest.json)
and [bounded profile summaries, configurations and trace hashes](validation/transaction-handles/profiles.json)
retain the evidence. Full traces and heap dumps are not checked into the source tree.

### Existing API residual costs

| Workload | Parent ops/s | Final ops/s | Paired change | Final/parent ratios |
| --- | --- | --- | --- | --- |
| Direct ordinary read | 85,935 [85,618–86,252] | 79,051 [77,294–80,808] | -8.0% | 0.903, 0.937 |
| Direct legacy begin/read/commit | 86,095 [85,946–86,244] | 75,096 [74,516–75,675] | -12.8% | 0.864, 0.880 |
| Direct attach/count/dispose | 16,448 [16,366–16,530] | 10,902 [10,846–10,957] | -33.7% | 0.663, 0.663 |
| Shared ordinary read | 49,848 [48,908–50,789] | 46,757 [46,143–47,370] | -6.2% | 0.933, 0.943 |
| Shared legacy begin/read/commit | 7,764 [7,664–7,865] | 7,826 [7,778–7,873] | +0.8% | 1.027, 0.989 |

| Workload | Allocated bytes/op, parent → final | Final window p95 / p99 (µs) |
| --- | --- | --- |
| Direct ordinary read | 10,264 → 10,248 | 15.0 / 19.9 |
| Direct legacy read | 10,192 → 10,128 | 15.7 / 20.8 |
| Direct attach/count/dispose | 29,208 → 29,920 | 107.6 / 127.0 |
| Shared ordinary read | 10,544 → 10,552 | 23.9 / 32.6 |
| Shared legacy read | 152,132 → 152,492 | 136.5 / 471.3 |

Attach includes count and disposal while another Direct facade retains the host.
Correct ownership adds fixed session/operation checks. Independent close scheduling
has a remaining cost even with reuse; the guards cannot simply be removed without
reopening the lifetime races tested above.

Default-tiering confirmation uses the same 10-second warmup/ten-window protocol,
with the DOTNET_TieredCompilation override removed. It is a separate configuration:

| Workload | Parent ops/s | Final ops/s | Paired change | Final/parent ratios |
| --- | --- | --- | --- | --- |
| Direct ordinary read | 187,336 [182,832–191,841] | 166,924 [166,880–166,968] | -10.8% | 0.870, 0.913 |
| Direct legacy read | 178,237 [177,924–178,549] | 147,689 [145,794–149,585] | -17.1% | 0.838, 0.819 |
| Direct attach/count/dispose | 39,434 [37,906–40,961] | 19,051 [18,440–19,662] | -51.6% | 0.450, 0.519 |

The default-tiering attach regression is materially larger than the tiering-disabled
result; neither configuration should be presented as a universal cost.

### New-handle costs and amortization

Both sides here use the final library. Legacy begin/work/commit is the semantic
adapter for an API that does not exist in the parent. Zero reads still includes
begin, GetCollection and commit. Costs are per transaction, not per row:

| Transaction | Legacy tx/s | Handle tx/s | Paired handle cost | Bytes/tx legacy → handle | Handle p95 / p99 (µs) |
| --- | --- | --- | --- | --- | --- |
| Shared, zero reads | 11,155 [10,912–11,397] | 1,448 [1,443–1,454] | 7.56×, 7.84× | 143,003 → 227,777 | 830.1 / 1198.4 |
| Shared, one read | 7,502 [7,110–7,894] | 1,311 [1,309–1,314] | 5.43×, 6.01× | 152,492 → 237,911 | 912.4 / 1288.0 |
| Shared, ten reads | 4,806 [4,770–4,842] | 1,129 [1,127–1,130] | 4.22×, 4.30× | 215,640 → 302,133 | 1126.2 / 1494.6 |
| Shared, 100 reads | 1,050 [1,042–1,058] | 651 [650–651] | 1.63×, 1.60× | 847,129 → 944,644 | 1917.3 / 2077.0 |
| Direct, one read | 64,301 [54,296–74,306] | 54,897 [47,201–62,593] | 1.19×, 1.15× | 10,128 → 11,520 | 23.0 / 27.3 |

Shared still creates a child engine and native holder per admitted handle and
releases native ownership before the next acquisition. The small-transaction
penalty remains substantial and amortizes as work per transaction grows. Pending
begins use caller threads without another child engine/holder. Local gates retain
inert metadata per distinct database/mutex key for the process lifetime; this PR
does not add eviction or consolidate every ordinary/legacy writer.

### Durable writes, contention and resource checks

Supplemental durable-write runs use five repetitions after a discarded warmup,
200 measured updates per database and durable commits enabled. They run in fixed
order within each process, parent process first, and are **not paired steady-state
estimates**. Host/storage variability is visible; these data do not establish
unchanged write performance or isolate code changes from time-varying load.
Every database is cold-reopened; same-value updates do not independently prove
individual durability acknowledgments. The model/fault tests establish those
ownership and recovery properties.

| Durable update | Parent ops/s | Final ops/s |
| --- | --- | --- |
| Direct ordinary | 194.0 [179.2–195.3] | 180.4 [176.3–195.7] |
| Direct legacy | 192.1 [189.2–192.7] | 185.1 [183.1–190.4] |
| Direct handle | — | 181.1 [171.8–194.6] |
| Shared ordinary | 159.5 [144.1–164.0] | 145.6 [126.7–157.3] |
| Shared legacy | 160.4 [155.9–160.8] | 152.7 [143.1–160.5] |
| Shared handle | — | 132.7 [95.1–144.5] |

Four-caller contention has three repetitions of 40 updates per caller to separate
rows in one collection, including final drain/disposal. Median [range] ops/s:

| API | Parent ops/s | Final ops/s |
| --- | --- | --- |
| Direct ordinary | 186.1 [178.9–187.7] | 172.5 [169.1–175.1] |
| Direct legacy | 185.7 [184.8–186.1] | 173.4 [170.9–180.9] |
| Direct handle | — | 177.0 [172.6–187.0] |
| Shared ordinary | 113.0 [112.3–114.5] | 105.5 [86.6–116.5] |
| Shared legacy | 110.4 [108.0–112.2] | 113.3 [108.2–115.7] |
| Shared handle | — | 125.7 [124.3–131.7] |

The Shared-handle contention result is not a general throughput improvement:
checkpoint time and WAL size were not separately measured. The resource exercise,
with and without a read callback, observes one extra holder thread while active,
twelve extra caller threads for twelve pending begins, and no additional sampled
native handles for those waiters. Warm samples move from 85 native handles to 88
while active, back to 85 after drain and 83 after session close. Thread counts
return after drain. First-run runtime initialization differs and is retained.
Drain plus final close was 9.4–21.6 ms. Active managed-heap deltas were approximately
256–287 KiB, including collectible objects; warm closed/collected heap samples were
434–436 KiB for the entire process. These are not isolated retained-object sizes.
Endpoint samples do not establish resource peaks; deterministic ownership tests
establish release. Idle close workers and the inert local-gate table are distinct
from a live engine or native database admission.

[Final steady-state raw data and commands](validation/transaction-handles/steady/manifest.json),
[computed paired summaries](validation/transaction-handles/steady/summary.json),
[environment and binary hashes](validation/transaction-handles/steady/environment.json),
and [all supplemental runs](validation/transaction-handles/supplement/manifest.json)
are retained. Use the [benchmark/summarizer instructions](../tools/TransactionHandleBenchmarks/README.md)
to reproduce them. [Earlier measurements](validation/transaction-handles/historical-performance.md)
are historical and are not the final results above.

**These material costs require an explicit maintainer decision before merge.**
The PR records that decision separately from this evidence. Passing safety
checks and bounded independent reviews do not accept the read/attach/Shared-handle
costs. Holder reuse/fair scheduling (#3069), participation/mapping consolidation
(#3017), coherent retained caches (#3004), and opt-in Direct-open waiting (#3068)
remain separate work.
