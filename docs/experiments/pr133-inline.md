# Rejected inline idle-close experiment

DIAGNOSTIC UPPER BOUND ONLY. Close runs its request/release synchronously when no active calls or handles exist. This fails the existing Idle_final_release_is_bounded_and_does_not_block_other_sessions regression: final resource release can block even with zero managed work. This branch must not be merged. Its benchmark quantifies the incentive for a future provably nonblocking detach fast path; it does not establish a safe implementation.

Baseline: c8c0cfab623a22880b1d71eb966b09e89f9153b9.

Cross-variant results and evidence are linked from JKamsker/LiteDB PR #133.
