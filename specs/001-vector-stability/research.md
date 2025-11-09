# Research Notes - Vector Stability Hardening

## Regression Summary

### Dot-Product maxDistance Normalization
- **Observed issue**: Metadata-provided `maxDistance` thresholds travel through the planner without the negation/normalization that `LiteQueryableVectorExtensions` applies, so dot-product searches silently widen result sets. Regression is visible when comparing metadata-driven queries to LINQ helpers on the same dataset.
- **Why it matters**: User Story 1 (P1) requires deterministic filtering; inconsistent normalization returns low-similarity documents and undermines confidence in vector search.
- **Signals/diagnostics**: New regression test in `LiteDB.Vector.Tests/VectorIndex_Tests.cs` must fail prior to the fix, proving the bug. Engine logs currently lack guardrails, so automated coverage is the primary detection mechanism.
- **Remediation outline**: Introduce a shared `NormalizeMaxDistance` helper in `LiteDB.Vector/Utils/VectorEnsure.cs`, store normalized values inside query metadata, and ensure projections read the normalized values.

### Legacy LiteQueryable Vector APIs
- **Observed issue**: `LiteQueryable.WhereNear/TopKNear` shims remain in the tree even though they never shipped. They mask the plugin-first story and require maintenance without delivering value.
- **Why it matters**: Removing them (US2) keeps the public surface minimal and prevents conflicting guidance between deprecated shims and `LiteQueryableVectorExtensions`.
- **Signals/diagnostics**: Code search for `WhereNear`/`TopKNear` still yields hits in client tests/docs; their presence complicates upgrades.
- **Remediation outline**: Delete the members plus doc references, reroute any sample/tests to use the plugin extensions, and confirm `dotnet build LiteDB.sln` succeeds without those symbols.

### Plugin Scope & Registry Isolation
- **Observed issue**: BSON type resolvers and page factory registries rely on globals that are mutated per database, causing collisions when vector-enabled and vanilla databases run side-by-side.
- **Why it matters**: User Story 3 (P3) targets stability for multi-database hosts; leaked global state can raise `PluginRequired` errors or corrupt serialization caches.
- **Signals/diagnostics**: Concurrent test runs show intermittent resolver mismatches; lacking per-context registries prevents deterministic behavior.
- **Remediation outline**: Bind registries to `ILitePluginContext` instances (inside `LiteDB/Document/BsonType.cs`, `LiteDB/Engine/Pages/PageFactoryRegistry.cs`, etc.) and provide isolation tests proving two contexts never overwrite each other.

### Artifact Cleanup & Ignore Hygiene
- **Observed issue**: `artifacts_temp/vector-followup/*.db` files were committed and are not ignored, so rerunning upgrade scripts dirties `git status`.
- **Why it matters**: User Story 4 (P4) ensures repository cleanliness and reduces noise in reviews.
- **Signals/diagnostics**: Running `scripts/vector/Invoke-VectorUpgrade.ps1` leaves tracked binaries under `artifacts_temp/`.
- **Remediation outline**: Delete committed `.db` artifacts, expand `.gitignore` to cover future outputs under `artifacts_temp/`, and update contributor docs with the expectation that upgrade outputs stay in ignored paths.
