# Transaction handle validation

This report covers the handle/lifetime changes layered on PR132. The parent is
`49c327cf1926fa300f9eb7477eb4bcb404f75c43`; production changes measured below end at
`cf7362a1f46ee885837397bba73404c1b030cc40`. Consult PR133 for the final tested head and
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

The focused final production-source runs pass 96 tests on net10.0 and 112 on
net8.0 (the latter also includes FuzzingContract). Earlier broader local partitions
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
| Candidate | `0772c09bbce7533d4183bdb60dceeb73348c59b8fbfd1b94ff10760936aba54d` |

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

## Performance and resource costs

Measurements use isolated production Release/net10.0 libraries with
`TestingEnabled=false`, Ubuntu 24.04 x64, .NET 10.0.11, SDK 10.0.400, local ext4
storage and `DOTNET_TieredCompilation=0`. Both use durable commits and identical
BSON/data shapes. No local build/test workload ran concurrently with measurements;
this shared development host can still have unrelated load. Timing is evidence for
this host and workload, not a portable speed claim.

See the [benchmark source and reproduction commands](../tools/TransactionHandleBenchmarks/README.md).
Ordinary and legacy workloads run unchanged against both DLLs. Handles use
begin/operation/commit as the semantic adapter; old code has no handle API.
Single-operation cases use five measured repetitions after warmup. Contention
uses three repetitions of four callers updating separate rows of one collection,
including final drain/disposal. Repetitions share one process in fixed workload
order; Shared read windows are short. Every database is cold-reopened and checked,
but same-value benchmark updates do not independently prove each commit: that
requires the model/fault tests above.

Performance tables and bounded raw samples accompany this report. Production
binary hashes, runtime, repetition ranges, allocations, latency percentiles and
thread/native-handle samples identify the tested configuration. Process memory
samples include runtime/harness overhead and are not exact per-handle live sizes.

Correct ownership adds synchronous operation/session checks to existing calls.
Direct attach/dispose also pays for session close coordination. Shared handles
create one child core and native holder for each admitted handle; they currently
cost substantially more than legacy transactions for small read-only work. Pending
begins allocate no additional core/holder but occupy their synchronous caller
threads. Completing a handle no longer forces a redundant final checkpoint; the
parent retains its final-checkpoint policy. These are explicit tradeoffs. Material
regressions require maintainer acceptance before merge under #3064/#3067.

### Measured costs

Median throughput (operations/s); brackets show the min–max throughput across
repetitions. `Attach` includes attach/count/dispose while another session is retained.

| Backend / API / operation | Parent | Candidate | Candidate p95 (µs) | Allocated bytes/op, parent → candidate |
| --- | ---: | ---: | ---: | ---: |
| Direct / ordinary / read | 92,878 [90,928–95,378] | 80,886 [69,266–84,824] | 12.8 | 10,058 → 10,066 |
| Direct / ordinary / write | 192 [191–193] | 193 [192–195] | 5,312.6 | 8,327 → 8,357 |
| Direct / ordinary / attach | 17,311 [16,516–17,473] | 11,955 [8,889–12,642] | 90.5 | 29,069 → 29,851 |
| Direct / legacy / read | 90,904 [86,675–92,565] | 71,935 [52,886–74,259] | 15.2 | 9,986 → 9,994 |
| Direct / legacy / write | 193 [192–193] | 192 [182–195] | 5,334.7 | 8,359 → 8,389 |
| Direct / handle / read | — | 61,393 [59,059–69,097] | 21.5 | — → 11,314 |
| Direct / handle / write | — | 191 [176–192] | 5,366.6 | — → 9,517 |
| Shared / ordinary / read | 52,367 [38,399–54,877] | 48,907 [45,730–50,412] | 22.1 | 10,453 → 10,474 |
| Shared / ordinary / write | 162 [161–164] | 162 [160–165] | 7,417.3 | 232,805 → 233,172 |
| Shared / ordinary / attach | 1,167 [1,140–1,181] | 1,121 [1,083–1,169] | 948.0 | 252,755 → 253,550 |
| Shared / legacy / read | 8,487 [6,471–8,734] | 8,289 [7,841–8,512] | 126.7 | 151,967 → 152,334 |
| Shared / legacy / write | 163 [161–165] | 163 [147–165] | 10,363.1 | 232,939 → 233,306 |
| Shared / handle / read | — | 1,319 [1,290–1,362] | 848.3 | — → 238,769 |
| Shared / handle / write | — | 149 [147–150] | 10,912.6 | — → 319,861 |

Ordinary Direct point-read throughput is about 13% lower; legacy Direct single-read
transactions about 21% lower; Direct attach/count/dispose about 31% lower. Durable
single-write medians are approximately unchanged for existing APIs. A Shared handle
for one read costs about 6.3 times as much as candidate legacy begin/read/commit;
its single-write throughput is about 9% lower than candidate legacy. The extra
child-engine/native-holder lifetime dominates small Shared operations. The guards
and close coordination add fixed costs to Direct work; these results do not isolate
each guard's individual cost.

Contention median operations/s (three repetitions, same four-caller workload):

| Backend / API | Parent | Candidate |
| --- | ---: | ---: |
| Direct / ordinary | 186.7 | 184.4 |
| Direct / legacy | 187.3 | 185.6 |
| Direct / handle | — | 185.8 |
| Shared / ordinary | 114.6 | 110.7 |
| Shared / legacy | 114.4 | 112.5 |
| Shared / handle | — | 129.7 |

Shared attach churn reached roughly 178–298 native threads in both versions, an
inherited transient cost not removed by this change.

The Shared contention result does not establish a general speedup: this workload
includes close/checkpoint scheduling and only three repetitions. WAL size and
checkpoint time were not separately measured, so no general lifecycle/storage
improvement is claimed. Candidate Shared
handle samples added one native holder thread; twelve pending begins added twelve
caller threads and no additional sampled native handles. After draining, thread
counts returned to the pre-handle baseline, including with read callbacks. Warm
samples increased native handle count by three while active, then returned after
handle drain; session close released two additional handles. First-run runtime
initialization differs and is preserved in the raw data. Drain plus final close
was 9.7–20.2 ms in this small workload. Active managed-memory deltas were roughly
262–290 KiB, including allocations that may be collectible; these are not isolated
retained-object sizes. Closed/collected warm samples were approximately 464–466 KiB
for the entire process's managed heap. Ownership-release tests, rather than these
coarse samples, establish that retained completed handles do not retain a live core.

[Raw samples and exact binary/source hashes](validation/transaction-handles/manifest.json)
are retained beside this report (five bounded JSONL files). They include all
repetitions, p50/p95/p99, allocation and process-resource samples. Maintainers must
explicitly accept the material read/attach/Shared-handle costs before merge;
passing safety and compatibility checks does not constitute that acceptance.
