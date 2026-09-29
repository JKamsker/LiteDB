# Transaction handle comparison

Build parent and candidate **production** Release libraries in separate checkouts
with `TestingEnabled=false`. Pin the parent to PR132 commit
`49c327cf1926fa300f9eb7477eb4bcb404f75c43`. Pass each absolute DLL path to this
same benchmark source; use separate intermediate/output directories. Set
`Handles=false` for the parent and `Handles=true` for the candidate.

```sh
dotnet build tools/TransactionHandleBenchmarks/TransactionHandleBenchmarks.csproj \
  -c Release -p:LibraryPath=/absolute/path/LiteDB.dll -p:Handles=true \
  -p:BaseIntermediateOutputPath=/tmp/handle-bench-obj/ -o /tmp/handle-bench
DOTNET_TieredCompilation=0 dotnet /tmp/handle-bench/TransactionHandleBenchmarks.dll COMMIT > measurements.jsonl
DOTNET_TieredCompilation=0 dotnet /tmp/handle-bench/TransactionHandleBenchmarks.dll COMMIT contention > contention.jsonl
```

Single-call tests use five measured fresh-database repetitions after a discarded
warmup repetition, with 50 warmup operations per measured database. Direct reads
measure 10,000 operations; other cases measure 200. `open` measures attach, count,
and disposal while one session remains open. Durable commits are enabled; writes
update an existing BSON row. Every database is cold-reopened and checked.

Contention measures three repetitions of four threads, each holding its own
database session and updating its own row 40 times in the same collection. Timing
includes draining/disposal; p50/p95/p99 cover individual operations. Process-wide
allocation and sampled native thread/handle counts include harness overhead.
Direct and Shared have the same workload, durability, storage directory and data.

New handles are compared with legacy begin/operation/commit as a semantic adapter,
not with a nonexistent old handle API. Report ordinary, legacy and new-handle
results separately, with runtime/platform, binary hashes, distributions and costs.
Avoid running other builds/tests concurrently with measurement. These are local
latency/resource measurements, not a universal throughput guarantee.

Candidate-only resource sampling also exercises twelve pending begins, with and
without a read callback:

```sh
DOTNET_TieredCompilation=0 dotnet /tmp/handle-bench/TransactionHandleBenchmarks.dll COMMIT resources > resources.jsonl
```

It samples idle, active, waiting, drained and closed/collected process state; the
waiting sample includes twelve application threads. These are coarse process
samples, not an assertion about exact per-object retained memory. Deterministic
pending-admission and holder-retirement assertions remain in the test suite.
