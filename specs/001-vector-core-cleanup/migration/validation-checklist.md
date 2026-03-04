# Migration Validation Checklist

- [ ] Priority 1 – Service factory relocation passes plugin bootstrap tests and build validation.
- [ ] Priority 1 – Legacy databases open with plugin-installed path without rebuilding vector indexes.
- [ ] Priority 2 – Public API extensions cover all deprecated overloads and emit guidance when plugin missing.
- [ ] Priority 2 – Performance benchmarks show ≤2% regression compared to baseline EnsureVectorIndex.
- [ ] Priority 3 – Query metadata bag flows through planner/executor without leaving vector fields in core.
- [ ] Priority 3 – Plugin absent scenario blocks vector queries with actionable diagnostics.
- [ ] Priority 4 – Plugin-managed BSON type registry loads existing vector documents unchanged.
- [ ] Priority 4 – Plugin page factory serves rebuild/snapshot paths within ≤2% throughput variance.
