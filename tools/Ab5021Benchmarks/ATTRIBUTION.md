# P1-D runner-only attribution

This extension starts from the identical-workload A/B harness at
`6f4ae96cea0d6ab51be165a46632f9da1e23627d`. Do not run either application or
`attribution.py` locally. Both entry points reject execution outside GitHub Actions.
Local builds and `python3 -m unittest discover -s tools/Ab5021Benchmarks -p test_attribution.py`
are permitted; the unit tests use synthetic data only.

The fork-only `perf-attribution.yml` push trigger runs Ubuntu, Windows and macOS,
each on net8/net10. Every job has six interleaved rounds, new processes, untimed
same-workload warmup, tiering disabled, Release and `TestingEnabled=false`.
All v6 configurations use `DurableCommits=false`. The input fixture, timers and
correctness checks are retained, with measured-section CPU/allocations/GC added.
Original `run.py` is historical; use the new orchestrator for this scratch branch.

Controls (changes relative to dev-fast):

- `v5`: pinned NuGet 5.0.21, native defaults.
- `dev-fast`: pinned dev b3c440623, default codec/cache/budget.
- `dev-legacy`: CompactStorage=Legacy; keeps modern checksum/durability protocol.
- `dev-pages`: TransactionPageLimit=100000; cache unchanged.
- `dev-cache`: CacheSize=128 MiB; transaction budget unchanged.
- `dev-throughput`: MemoryProfile.Throughput (128 MiB, 4000 pages).
- `dev-legacy-pages-cache`: combined codec/budget/cache control, for interactions.
- `v5-no-checkpoint` / `dev-no-checkpoint`: automatic CHECKPOINT=0 on both
  engines, to separate checkpoint-triggered work from the single-insert gap.
  Explicit checkpoints and graceful close still run outside that insert timer.
- `write-base-fast` / `write-proto-fast`: pre-existing prototype's exact base
  7b71bc4dd and exact reviewed revision 0538d2b95, same harness and settings.
  Compare that pair to attribute the prototype bundle; comparing its older base
  directly to current dev does not isolate its changes.

The unchanged-tree merge parent retains the pre-existing prototype's history so
GitHub can check it out. No new product-code change is introduced. CI verifies
the scratch tree's LiteDB and root build files match pinned dev. Separate source
worktrees isolate production builds from each other and any hook-enabled build.

Linux jobs then collect separate diagnostic processes for v5, dev-fast,
dev-legacy, dev-pages, write-base-fast and write-proto-fast:

- dotnet-trace sampled thread time + GC allocation events, three fresh measured
  fixtures per process. Stable non-inlined `Measurement` wrappers identify each
  measured scenario; warmup/setup/verification/disposal are excluded from analysis.
  Raw nettrace is retained. EventPipe samples can include blocked time and are
  not CPU-only shares. Inclusive method shares overlap; the category partition
  uses an explicitly ordered first-match rule and is only a descriptive view.
- dotnet-counters System.Runtime CSV: whole-process GC/heap/runtime timeline.
  Exact scenario allocation/GC deltas also come from the uninstrumented runner.
- strace -f -c: whole-process syscall counts, including setup/disposal.
  A second detailed trace attributes counts to stderr BEGIN/END markers;
  these include small measurement-counter overhead adjacent to the workload.
  Summed syscall duration is traced thread time, not exact wall time saved.
- perf cpu-clock sampling for v5/dev-fast, including managed perf maps when
  resolvable. Kernel-only partitions exclude unresolved or unmarked samples;
  inspect symbol coverage before interpreting them. Kernel sampling failure
  remains an explicit diagnostic gap, never a zero-percent attribution.

`summary.json` contains exact scenario medians, ranges, CPU, allocation and GC
counts. `sample-attribution.json`, `kernel-cpu-attribution.json` and
`syscall-attribution.json` contain scoped diagnostics. `attribution.md` ranks
configuration savings by scenario. These are same-runner comparisons; do not
pool runtimes or operating systems. Point reads remain a regression control.
Cache capacity controls do not isolate cache implementation overhead. Compact
Legacy changes physical density as well as encoding, so its delta is net
end-to-end codec-mode impact, not a pure helper CPU cost. No checksum, padding,
flush, ownership or safepoint implementation is bypassed.

Download after completion (the step brief prohibits waiting for CI):

```bash
gh run download RUN_ID -R JKamsker/LiteDB -D /tmp/litedb-proto/phases/p1d-artifacts/
python3 tools/Ab5021Benchmarks/analyze_attribution.py /path/to/one/artifact
```

Every optimization remains a hypothesis until runner outputs support it. The
research harness only verifies ordinary operation and graceful reopen. It does
not supply crash/power-loss safety evidence for a future implementation.
