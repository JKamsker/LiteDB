# Shared teardown getter waits for its own closing core

Uses the production sources in [SharedSelfTeardownProof](../SharedSelfTeardownProof/README.md)
in `--wait-only` mode. The known-bad package is built from PR #133 commit
`3abead6dc2eb6413daf5fa81d8faed06cdf4ade7` as `0.0.0-knownbad.3abead6dc2eb`.
Source must refuse same-instance getter reentry during executing core teardown.
Plain and encrypted cases plus positive controls are required. The observed
closing-core wait is specific to the transaction-handle branch; this proof makes
no claim that upstream dev has that same dependency graph.
