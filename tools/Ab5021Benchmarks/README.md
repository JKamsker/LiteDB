# Hosted LiteDB 5.0.21 / dev comparison

Scratch research harness. Do not run measurements locally. The workflow is guarded
to `JKamsker/LiteDB`, branch `perf/ab-5021-vs-dev`, and pins the library tree to
`b3c4406231908c6ca29f6d4b2769aa08351d6fc2`. No library or existing CI is changed.

Both projects compile the same C# files, against NuGet **[5.0.21]** or the dev
ProjectReference. Both target net8.0 and net10.0, Release, TestingEnabled=false.
The only conditional API use selects dev's documented `DurableCommits` setting.
Default dev (`true`) and closest v5 equivalent (`false`) are both measured.
Turning it off does not remove dev's checksum, checkpoint or creation sync work.
v5 has no commit fsync; it still syncs database creation and checkpoint writes.

Per OS/runtime job: six fresh child processes per configuration, alternating
`v5, dev-default, dev-os-cache` and the reverse. Each child first runs an untimed
2k-document/100-small-transaction workload. Tiered compilation is disabled for
all children to avoid comparing tier-0 and optimized code. Every child creates
its own unique local-temp directory and deletes only that directory on exit.
The fixed seed is 5021. Data: explicit integer id, unique secondary integer Key,
unindexed integer Value, 96-character fixed-seed hexadecimal payload. Ordinal
invariant collation, Direct (exclusive writer), unencrypted, no initial size.

Each round uses a fresh main database, a separate small-transaction database and
a separate checkpoint database. Main scenarios run in the listed order; their
state is identical by construction. Main and small DBs retain CHECKPOINT=1000.
Bulk insert includes BeginTrans, Insert(100k), Commit and any automatic checkpoint.
Point lookups retrieve 10k full documents by indexed Key using fixed random ids;
full scan materializes 3x100k documents through an unindexed `Value >= 0` query.
These are warm-cache measurements, after an untimed full contents check.
Update and delete each affect the first 10k docs in one explicit transaction.
Rebuild operates on the remaining 90k docs with the same primary/secondary indexes.
Open/close measures 20 sequential writable opens of that checkpointed database,
including collection-name enumeration and disposal; OS file cache is not cleared.
Explicit checkpoint starts with 100k docs in uncheckpointed WAL (CHECKPOINT=0
on both libraries for this scenario only). It measures committed-page copying
and log retirement, not a guarantee of shrinking the physical WAL.

Every stage checks counts, checksums, indexed lookup results and/or document
contents. Main, small and checkpoint DBs are reopened and validated. These are
benchmark correctness checks, not crash/power-loss safety qualification. No storage
protocol, fault hook, coverage filter or existing test is changed by this harness.

JSON preserves each round's runtime, assembly hash and timings; CSV includes raw
samples, medians, minima, maxima, spreads and dev/v5 ratios. Directory bytes are
cumulative (all scenario files and retained rebuild backups), not per-scenario
allocation. Ratios compare medians within a single OS/runtime job only. Child
startup, data generation, warmup, verification and cleanup are outside timings.
