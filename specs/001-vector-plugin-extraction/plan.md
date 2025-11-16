# Implementation Plan: Vector Plugin Isolation

**Branch**: `[001-vector-plugin-extraction]` | **Date**: 2025-11-16 | **Spec**: `specs/001-vector-plugin-extraction/spec.md`
**Input**: Feature specification from `/specs/001-vector-plugin-extraction/spec.md`

## Summary

LiteDB must remain fully functional without LiteDB.Vector, while all vector search functionality (indexing, BSON types, metadata, query operators) moves entirely into the plugin. The plan introduces generic extension points (custom BSON handlers, index metadata serializers, page factories) in core, shifts vector implementations to LiteDB.Vector, and enforces deterministic handling when vector-enabled databases are opened without the plugin.

## Technical Context

**Language/Version**: C# 12 targeting .NET Standard 2.0 & .NET 8.0  
**Primary Dependencies**: LiteDB core library, LiteDB.Plugins infrastructure, LiteDB.Vector plugin (optional)  
**Storage**: LiteDB file storage (BSON pages, vector metadata persisted as plugin-managed blobs)  
**Testing**: xUnit + FluentAssertions (LiteDB.Tests, LiteDB.Vector.Tests) plus integration stress scripts  
**Target Platform**: Cross-platform .NET (Windows, Linux, macOS) via netstandard2.0/net8.0  
**Project Type**: Multi-project library repo (LiteDB core, LiteDB.Vector, test suites, tools)  
**Performance Goals**: ≤2% regression in vector benchmark suites; no measurable cost for non-vector scenarios  
**Constraints**: Plugin optionality enforced; deterministic behavior for pre-release vector prototype databases (created during internal testing, not yet in production) when the plugin is absent; diagnostics must remain actionable (diagnostics explicitly cite LiteDB.Vector plugin ID per research); prerelease vector formats are not upgraded—vector operations simply fail with `LITE2002` until LiteDB.Vector is installed  
**Scale/Scope**: Touches core engine (indexing, storage, query), plugin framework, LiteDB.Vector, and both test suites; prototype vector database handling policy (default = allow database open but refuse vector-dependent operations so forward-compat remains covered before public release)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Principle I (Library-First): Core remains authoritative; extension points stay within LiteDB/ domains. PASS
- Principle II (Testing Discipline): Plan mandates updates in LiteDB.Tests & LiteDB.Vector.Tests covering optional plugin behavior. PASS
- Principle VI (Plugin-First): Vector search becomes purely plugin-owned; aligns directly with the new principle. PASS
- No gate violations identified; Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-vector-plugin-extraction/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md        # created later by /speckit.tasks
```

### Source Code (repository root)

```text
LiteDB/                     # Core engine, document, client, plugin abstractions
LiteDB.Vector/              # Plugin library that will now own all vector behavior
LiteDB.Tests/               # Core tests (Engine, Document, Client)
LiteDB.Vector.Tests/        # Plugin-specific tests
specs/001-vector-plugin-extraction/  # Feature docs/assets
```

**Structure Decision**: Use the existing multi-project repository structure—core modifications stay in `LiteDB/`, plugin code in `LiteDB.Vector/`, and coverage in `LiteDB.Tests/` plus `LiteDB.Vector.Tests/`. Feature docs live under `specs/001-vector-plugin-extraction/`; no new projects required.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _None_ |  |  |

## Requirement & Success Criteria Mapping

### Functional Requirements → Tasks

| Requirement | Task Coverage | Notes |
|-------------|---------------|-------|
| FR-001 (core exposes only neutral extension points) | T003, T007, T008, T009, T010, T011 | Removes public `Vector*` members, routes interception through plugin registries, and proves the API surface via optionality tests. |
| FR-002 (LiteDB.Vector owns end-to-end vector features) | T009, T015, T020 | Rebuilds plugin extensions, registers metadata/page factories, and wires query/BSON hooks from LiteDB.Vector. |
| FR-003 (plugin-managed metadata serializers) | T012, T013, T015, T016 | Replaces CollectionPage slots, updates rebuild/file reader flows, registers LiteDB.Vector serializers, and verifies compatibility. |
| FR-004 (plugin-provided query operators/planner rules) | T017, T019, T020, T021 | Removes vector tokens from core, defers planning to plugin rules, and tests planner behavior with/without the plugin. |
| FR-005 (plugin-defined BSON handlers) | T004, T018, T020 | Introduces the BSON registry, removes vector handlers from core, and registers the handler inside LiteDB.Vector. |
| FR-006 (diagnostics cite plugin ID) | T005, T014, T015, T016 | Adds the diagnostic policy, enforces plugin-required errors when accessing protected assets, and verifies payloads via tests. |
| FR-007 (docs/samples describe plugin optionality) | T001, T022, T023 | Updates migration guides, README/docs, and the quickstart walkthrough to highlight plugin installation steps. |
| FR-008 (deterministic behavior without plugin) | T014, T016 | Implements the “warn once, throw on access” policy and exercises it against prototype databases. |
| FR-009 (generic plugin metadata/page registries) | T012, T013, T015 | Swaps vector-specific metadata/page hooks for plugin-agnostic registries and ensures the plugin re-registers its descriptors. |
| FR-010 (fail prerelease vector assets with single diagnostic) | T014, T016 | Enforces the `LITE2002` policy and proves it via compatibility tests covering plugin-absent scenarios. |

### Success Criteria → Tasks

| Success Criterion | Task Coverage | Validation Mechanism |
|-------------------|---------------|----------------------|
| SC-001 (`rg "Vector" LiteDB` clean outside hooks) | T002, T007-T010 | `scripts/verify-vector-clean.ps1` enforces the grep check after vector APIs move to the plugin. |
| SC-002 (API surface unchanged aside from removals) | T007, T008, T011 | Optionality tests plus API diff review during T011 confirm the LiteDB-only assembly no longer exposes vector symbols. |
| SC-003 (=2% regression ceiling with plugin installed) | T009, T020 | Vector benchmarks run as part of plugin extension rewrites; regressions gated before closing Phase 5. |
| SC-004 (consolidated plugin absence diagnostics) | T005, T014, T015 | Diagnostic policy work and engine enforcement emit a single `VectorCompatibility.PluginRequired` path validated by tests. |
| SC-005 (deterministic behavior with/without plugin) | T014, T016 | VectorMetadataCompatibilityTests exercise prototype files both ways, asserting warning + targeted failures only. |
