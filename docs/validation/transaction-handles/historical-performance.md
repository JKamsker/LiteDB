# Historical transaction handle measurements

The following measurements predate review fixes and are **not final costs**.



The measurements below predate the admission and close-scheduling review fixes.
They are retained as a baseline, not final-revision results or accepted costs.
Final profiling and longer paired measurements follow correctness verification.

Measurements use isolated production Release/net10.0 libraries with
`TestingEnabled=false`, Ubuntu 24.04 x64, .NET 10.0.11, SDK 10.0.400, local ext4
storage and `DOTNET_TieredCompilation=0`. Both use durable commits and identical
BSON/data shapes. No local build/test workload ran concurrently with measurements;
this shared development host can still have unrelated load. Timing is evidence for
this host and workload, not a portable speed claim.

See the [benchmark source and reproduction commands](../../../tools/TransactionHandleBenchmarks/README.md).
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

[Raw samples and exact binary/source hashes](manifest.json)
are retained beside this report (five bounded JSONL files). They include all
repetitions, p50/p95/p99, allocation and process-resource samples. Maintainers must
explicitly accept the material read/attach/Shared-handle costs before merge;
passing safety and compatibility checks does not constitute that acceptance.
