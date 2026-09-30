# Reuse the bound transaction scope

Internal TransactionEngine adapters bypass the ordinary SessionEngine binding-clearing wrapper. Dispatch checks the established session/transaction and retains its one-use engine ticket. Public Run guards, session admission, callback rejection and completion remain. This is a separately measured research variant; no combined optimization claim.

Baseline: c8c0cfab623a22880b1d71eb966b09e89f9153b9.

Cross-variant results and evidence are linked from JKamsker/LiteDB PR #133.
