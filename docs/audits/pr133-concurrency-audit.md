# PR #133 concurrency audit

## Frozen baseline and finite gate

The starting PR head is `98a10086c2a296040a345e62ea987d59fc2ab487`,
stacked on PR #132, with base `5dd942a7367c361fadd600be4ce10aace2768b27`.
The baseline production tree is `4c82e64ec435862987ccb0d1061dc6eadc34510f`.
Its full CI and Safety aggregate passed before this audit. Those results do not
establish coverage of the new schedules or qualify later audit commits.

This audit is bounded to transaction/session/reader ownership, Shared native
admission and retirement, callbacks, maintenance and process death. It does not
attempt arbitrary application-level wait graphs or physical device power loss.
The existing modeled persistence-fault targets remain separate evidence.

The gate consists of:

1. An acquisition-site inventory and resource dependency graph for the affected
   lifecycle, with each identified feasible cycle tied to a deterministic case
   or a concrete justification that the cycle cannot occur.
2. A reusable deterministic actor explorer in the existing test/fuzz infrastructure,
   covering selected pairwise and three-actor interleavings across Direct/Shared,
   plain/encrypted and same/peer facades. Recorded schedules must establish actual
   overlap and support replay, with an independent committed-state oracle.
3. A multiprocess campaign in the existing fuzz runner, with observed ownership
   and persistence boundaries, parent-recorded acknowledgements, independent
   progress watchdogs, and complete permitted cold-recovery outcomes.
4. An adversarial oracle review with deliberate faults and relevant pinned bad
   revisions where available. Mutants validate oracles; they do not establish
   historical production regressions.
5. A small deterministic PR selection, a larger recorded local campaign and an
   extended scheduled configuration. Counts, seeds, exact source/runtime identity
   and outcomes are recorded after execution, not inferred from configuration.

No production functionality or performance optimization is part of this audit.
A confirmed production defect retains a minimal failing reproducer and a separate
correction proposal. Such a defect blocks the affected merge decision until fixed
and revalidated; it must not be converted into passing coverage by accepting an
unexpected timeout, exception or partial outcome.

## Reports

- [Acquisition inventory and dependency graph](pr133-concurrency-dependencies.md)
- [Independent oracle review](pr133-concurrency-oracle-review.md)
- [Coverage matrix](pr133-concurrency-matrix.md)

## Current result

In progress. No merge recommendation is made until the selected campaign and
independent review are complete. Remaining states and platform limits will be
listed explicitly; absence of a discovered bug is finite evidence, not proof of
universal freedom from deadlock.
