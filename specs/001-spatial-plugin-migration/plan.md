# Implementation Plan: Spatial Plugin Migration

**Branch**: `001-spatial-plugin-migration` | **Date**: 2025-11-01 | **Spec**: [specs/001-spatial-plugin-migration/spec.md](spec.md)  
**Input**: Feature specification from `/specs/001-spatial-plugin-migration/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Decouple spatial functionality from the LiteDB core by extending plugin extension points (factory-based LINQ resolvers, richer query planning rules, index interceptors, queryable extensions), removing spatial code paths from the core library, and reinstating spatial capabilities exclusively through the spatial plugin packages with updated documentation for first-release adopters.

## Technical Context

**Language/Version**: C# targeting .NET Standard 2.0 and .NET 8.0  
**Primary Dependencies**: LiteDB core, LiteDB plugin framework, LiteDB.Spatial.* packages, xUnit + FluentAssertions  
**Storage**: LiteDB embedded database (existing engine)  
**Testing**: `dotnet test` (xUnit suites plus spatial/vector specific tests)  
**Target Platform**: Cross-platform .NET (Windows/Linux/macOS) multi-targeted assemblies  
**Project Type**: Library with opt-in plugin packages  
**Performance Goals**: Preserve current spatial query performance; maintain existing engine benchmarks; reduce core NuGet package size by ≥5 MB  
**Constraints**: No nullable warnings on either target; plugin architecture must remain opt-in; maintain public API compatibility for non-spatial consumers  
**Scale/Scope**: Changes touch `LiteDB/` (Client, Document, Engine, Plugins), spatial plugin projects (`LiteDB.Spatial.*`), `LiteDB.Tests/`, `LiteDB.Spatial.Core.Tests/`, and `docs/`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Principle I – Library-First Architecture**: Plan preserves core/plugin boundaries by relocating spatial logic into dedicated packages.  
- **Principle II – Testing Discipline**: New and adjusted tests planned in `LiteDB.Tests` and `LiteDB.Spatial.Core.Tests` for plugin-enabled/disabled scenarios.  
- **Principle IV – Performance-First Development**: Benchmark/stress runs required for query planner and engine modifications.  
- **Principle VI – Plugin-First for Non-Core Features**: Migration enforces plugin delivery for spatial features.  
✅ All gates satisfied; proceed with Phase 0 research.

## Project Structure

### Documentation (this feature)

```text
specs/001-spatial-plugin-migration/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md          # (created by /speckit.tasks, not this command)
```

### Source Code (repository root)

```text
LiteDB/
├── Client/
├── Document/
├── Engine/
├── Plugins/
└── Utils/

LiteDB.Spatial/
LiteDB.Spatial.Core/
LiteDB.Spatial.Cartesian2D/
LiteDB.Spatial.Cartesian3D/
LiteDB.Spatial.Geographic/

LiteDB.Tests/
LiteDB.Spatial.Core.Tests/

docs/
└── spatial-*.md
```

**Structure Decision**: Utilize the existing library-first layout. Core modifications live under `LiteDB/` (especially `Client/Mapper`, `Document/Expression`, `Engine/Query`, `Plugins/`). Spatial capability is delivered via the sibling plugin projects (`LiteDB.Spatial*`). Tests reside in `LiteDB.Tests/` and `LiteDB.Spatial.Core.Tests/`, and documentation updates land in `docs/`.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
