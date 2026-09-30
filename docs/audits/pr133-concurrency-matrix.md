# PR #133 concurrency coverage matrix

Baseline: `98a10086c2a296040a345e62ea987d59fc2ab487`.

Each row links the acquisition inventory to an observed schedule and its oracle.
A configured but unexecuted boundary is a gap, not passing evidence. Resource IDs
are defined in the [dependency inventory](pr133-concurrency-dependencies.md).

| Blocking boundary | Resources already held | Competing operation | Expected behavior | Existing/new test and observed boundary | Result or gap |
| --- | --- | --- | --- | --- | --- |

This matrix is populated from the independent inventory and executed explorers.
Feasible cycles require a deterministic reproduction or an explicit impossibility
argument. Application-created cross-thread callback dependencies that cannot be
inferred by the engine must be distinguished from engine-internal lock cycles.
