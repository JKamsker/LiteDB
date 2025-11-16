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
**Constraints**: Plugin optionality enforced; deterministic behavior for pre-release vector prototype databases (created during internal testing, not yet in production) when the plugin is absent; diagnostics must remain actionable (diagnostics explicitly cite LiteDB.Vector plugin ID per research)  
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
