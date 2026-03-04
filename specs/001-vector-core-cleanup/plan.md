# Implementation Plan: Vector Core Cleanup

**Branch**: `001-vector-core-cleanup` | **Date**: 2025-11-02 | **Spec**: C:\Users\Jonas\repos\private\JKamsker\LiteDB\specs\001-vector-core-cleanup\spec.md  
**Input**: Feature specification from `C:\Users\Jonas\repos\private\JKamsker\LiteDB\specs\001-vector-core-cleanup\spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Inventory every `Vector` occurrence inside `LiteDB/`, classify its coupling to vector search, and define the migration path so the functionality can live inside `LiteDB.Vector`. The effort documents current touch points, identifies plugin infrastructure gaps (query metadata, BSON types, storage hooks), and sequences work to deprecate core APIs in favor of plugin-managed extensions without breaking existing databases.

## Technical Context

**Language/Version**: C# (`<LangVersion>latest</LangVersion>` targeting `netstandard2.0` + `net8.0`)  
**Primary Dependencies**: LiteDB core library, emerging plugin infrastructure under `LiteDB/Plugins`, `LiteDB.Vector` package for the destination implementation  
**Storage**: LiteDB document store files (`*.db`) with BSON serialization and custom page formats  
**Testing**: xUnit + FluentAssertions via `LiteDB.Tests` and `LiteDB.Vector.Tests`  
**Target Platform**: Cross-platform .NET (Windows/Linux/macOS) through multi-targeted libraries  
**Project Type**: Multi-project .NET library with companion plugin packages  
**Performance Goals**: Preserve current vector index/query throughput after migration (acceptable regression budget NEEDS CLARIFICATION)  
**Constraints**: Must avoid breaking existing databases and provide graceful errors when plugin absent; required plugin extension points for BSON, query planning, and storage pipelines NEEDS CLARIFICATION  
**Scale/Scope**: Entire `LiteDB/` source tree (Engine, Document, Client, Utils) plus `LiteDB.Vector` integration surface

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I – Library-First Architecture**: Plan removes non-core vector logic while keeping core abstractions coherent. ✔️ PASS
- **Principle II – Testing Discipline**: Cleanup roadmap includes verification strategy relying on existing test suites and focused runs. ✔️ PASS
- **Principle VI – Plugin-First for Non-Core Features**: Migration explicitly relocates vector functionality into the plugin and hardens plugin extension points. ✔️ PASS
- **Gate Status**: PASS — proceed to Phase 0 research with emphasis on clarifying plugin extension requirements and performance budgets.

## Project Structure

### Documentation (this feature)

```text
C:\Users\Jonas\repos\private\JKamsker\LiteDB\specs\001-vector-core-cleanup\
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts\
└── tasks.md  # created by /speckit.tasks
```

### Source Code (repository root)

```text
C:\Users\Jonas\repos\private\JKamsker\LiteDB\
├── LiteDB\
│   ├── Client\
│   │   ├── Database\
│   │   └── Shared\
│   ├── Document\
│   │   ├── Json\
│   │   └── Serialization assets (BsonType, BsonValue, BsonVector)
│   ├── Engine\
│   │   ├── Engine\
│   │   ├── Pages\
│   │   ├── Query\
│   │   ├── Services\
│   │   └── Structures\
│   ├── Plugins\  # shared plugin infrastructure (ILitePlugin, contexts)
│   └── Utils\
├── LiteDB.Vector\
│   ├── Engine\
│   ├── Expressions\
│   ├── Extensions\
│   ├── Query\
│   └── Utils\
└── LiteDB.Vector.Tests\
    └── Vector search regression coverage
```

**Structure Decision**: Feature spans the existing LiteDB core library and the `LiteDB.Vector` plugin package. Documentation artifacts live under `specs/001-vector-core-cleanup`, while implementation work will modify files within `LiteDB/` directories listed above and extend the plugin project (`LiteDB.Vector/`). No new projects are introduced; focus is on reshaping boundaries and infrastructure hooks across the existing solution.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
