# Runner-only CP0 commit research

Scratch branch `v6/p1k-commit-residual`; dispatch the already registered
`perf-attribution.yml` workflow on **JKamsker/LiteDB**. No upstream dispatch,
performance acceptance threshold change, product edit or product candidate.

Pinned Phase 1 `a2664ea415e1e574bc5c8f28cb0c484c1bb898c7`, historical dev
`b3c4406231908c6ca29f6d4b2769aa08351d6fc2`, NuGet 5.0.21, plus its nuspec
repository commit `391cc9318c5be6e56cb71b3ce14b1ed9cb324763` as a diagnostic
source proxy (rebuilt with the current SDK, not assumed binary-identical to NuGet).

The 5,000 deterministic typed documents and unique Key index follow the P1-D
fixture. Both libraries use CHECKPOINT=0, and modern libraries explicitly set
DurableCommits=false. Each process performs a full 5,000-operation warmup, then
fresh-file measurement; tiering is disabled. Six alternating rounds on Linux and
Windows, twelve on macOS. A Legacy control isolates the mode effect, including
schema/storage layout effects; it does not isolate encoder CPU. Full documents
and selected index lookups are verified outside the timer, before explicit
checkpoint and after file-backed reopen. CP0 is diagnostic only.

Three **separate disposable source trees** instrument the actual method bodies
with nested Stopwatch scopes. Generated sources never enter the branch diff.
Exclusive wall times partition only the active measured window. `other` retains
unclassified insertion/index/mapping work. Each category includes probe/JIT
perturbation; tiny negative-eligibility and owner checks cannot be called free or
assigned exact production nanoseconds. Compare diagnostic totals to their own
uninstrumented source controls. The v5 source control is also compared with
NuGet; a material mismatch blocks using source timing as exact package timing.
No category is an additive forecast of a safe optimization's speedup.

`instrumentation-*.json` records every boundary. Iterator local functions are
not wrapped across yields. Scope counts for DEVICE-SYNC and CHECKPOINT must be
zero during the measured source diagnostic windows on every OS. Group getter
counts and time are retained; GROUP-ACTIVE constructor/coordination counts must
also be zero. No grouping is enabled or elided by the experiment. Null group
dependency/version-field reads remain charged to their surrounding WAL-index scope.

Linux also profiles the **uninstrumented** NuGet/dev/Phase 1 variants with
dotnet-trace, runtime allocation events, and fd-resolved strace between explicit
BEGIN/END markers. EventPipe stacks are sampled thread time including off-CPU
intervals, not a CPU-only causal partition. Process CPU deltas are recorded
separately. TraceRead requires measured Single stacks and allocation ticks;
missing boundaries fail collection. Inclusive methods overlap. Allocation ticks
are approximate; timed allocation deltas include process background activity.
Summed strace durations are perturbed thread time, not recoverable latency.

Offline analysis: `python -m unittest discover -s tools/CommitResidualBenchmarks -p
'test_commit*.py' -v`. `commit-summary.json` contains production totals and ranked
diagnostic ms/commit; `*.events.json` contains scoped stack/allocation dictionaries;
`syscall-per-commit.json` contains syscall counts, durations and EOF transitions.
Raw traces/logs/manifests/environment/assembly hashes are retained for 30 days.
Windows/macOS use no ETW; failures are evidence gaps, never performance ratios.
