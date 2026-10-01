# PR 133: handle admission from a Shared close callback

A Shared connection closes by checkpointing through a caller-owned data stream.
That stream's synchronous write callback opens a transaction handle through a
normal, filename-backed peer facade. The callback retains native ownership;
the handle's separate child cannot acquire it until the callback returns.

This integration gap exists at real PR commit
`be60d53a35088e408d8265989ed915cea1983ec1`, pinned as
`0.0.0-knownbad.be60d53a3508`. The ordinary peer-write guard consults
`SharedCallFrames`, but this revision's handle preflight consults only the older
public-call/read scopes. No production code is modified by this reproduction.

Four cases run: same-file and other-file callbacks, each plain and encrypted.
The same-file case uses default-infinite admission with a cancellation token.
An independent controller observes the actual cached transaction child in its
native-wait region, the outer owner still held, an active callback, and failure
to acquire that native mutex from the controller. It confirms the condition
again after 50 ms, then cancels strictly for cleanup. Elapsed time alone, a
missing callback or an unexpected exception cannot establish reproduction.

- Known-bad: exit `0`, `NATIVE_SELF_WAIT_VERIFIED`; both native dependencies were
  observed and diagnostic cancellation drained both closes.
- Fixed: exit `2`, `FIXED_VERIFIED`; both same-file calls raised the exact
  immediate handle refusal, while both other-file handles committed.
- Configuration errors, mixed outcomes, missing observation fields, unexpected
  errors or unjoined workers: exit `1`, satisfying neither expectation.

Every completed case proves a later handle can commit, then cold-opens twice
and compares exact rows/payloads, an actual secondary index seek and the complete
sentinel. A live worker prevents disposal or cold inspection. All original
fixtures remain under a short OS temporary directory. Its physical canonical
path is used consistently for caller streams and filename-backed connections,
including Darwin's `/var` alias; this does not relocate the fixture. On Darwin,
the callback FileStream uses the same raw-descriptor opening as production
`AdmittedFileStream`, avoiding the path constructor's automatic whole-file flock
while preserving the engine's native ownership and admission protocol. Other
platforms retain ordinary path-based FileStream construction.

```bash
python .github/scripts/regression_proof.py pack-known-bad \
  --commit be60d53a35088e408d8265989ed915cea1983ec1 --feed artifacts_temp/knownbad-feed
dotnet run --project LiteDB.ReproRunner/LiteDB.ReproRunner.Cli -- \
  run Issue_133_SharedCloseCallbackBegin
```

Set `RestoreAdditionalProjectSources` to the absolute local feed path when
running the packed known-bad variant. The regression registry and CI entries run this proof separately; the project is
not included in the ordinary solution because it needs the local package feed.
