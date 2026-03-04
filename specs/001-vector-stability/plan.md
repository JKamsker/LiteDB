# Implementation Plan: Vector Stability Hardening

**Branch**: [001-vector-stability] | **Date**: 2025-11-02 | **Spec**: specs/001-vector-stability/spec.md
**Input**: Feature specification from /specs/001-vector-stability/spec.md

## Summary

Address four regressions around vector search stability: (1) add a failing regression test for dot-product maxDistance normalization before fixing the planner/metadata pathways, (2) remove the undeployed LiteQueryable vector APIs entirely instead of maintaining shims, (3) eliminate reliance on any global plugin registry by scoping BSON/page factories strictly to each LiteDatabase instance, and (4) clean git history by removing committed vector upgrade artifacts and ensuring rtifacts_temp/ stays ignored. Implementation touches LiteDB.Engine, LiteDB.Vector, LiteDB.Client, plus unit/integration tests.

## Technical Context

**Language/Version**: C# targeting .NET Standard 2.0 + .NET 8.0 (multi-targeted library)  
**Primary Dependencies**: LiteDB core library, LiteDB.Vector plugin, xUnit + FluentAssertions for testing  
**Storage**: File-backed embedded data files managed by LiteDB  
**Testing**: dotnet test using solution-wide test suites; add cases in LiteDB.Tests and LiteDB.Vector.Tests  
**Target Platform**: Cross-platform .NET consumers (desktop/server/mobile)  
**Project Type**: Library + plugin packages (LiteDB, LiteDB.Vector, companion tests)  
**Performance Goals**: Maintain current vector query throughput and latency; no additional allocations on the hot path  
**Constraints**: Must preserve plugin boundary integrity (Principle VI), no nullable warnings, and follow Allman formatting  
**Scale/Scope**: Impacts shared query planner, plugin context initialization, and repo hygiene (medium-sized change touching ~5-7 files plus tests)

## Constitution Check

*GATE (pre-Phase 0):*  
- **Principle I (Library-First)**: Work remains within LiteDB/ and LiteDB.Vector/; no new companion app code. ✅  
- **Principle II (Testing Discipline)**: Plan mandates a failing regression test before the dot-product fix plus new isolation tests. ✅  
- **Principle III (API Documentation)**: Removing undeployed APIs requires doc cleanup but no new public surface. ✅  
- **Principle IV (Performance)**: Registry refactor must avoid global locks/hot-path overhead; plan includes benchmarking sanity before merge. ✅  
- **Principle V (Cross-Platform)**: Changes limited to managed code; ensure compilation on both target frameworks. ✅  
- **Principle VI (Plugin-First)**: Reinforces plugin isolation by eliminating global registries. ✅  

No gates blocked; proceed to Phase 0 research without violations.

## Project Structure

### Documentation (this feature)

`	ext
specs/001-vector-stability/
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 research notes
├── data-model.md        # Phase 1 artifact
├── quickstart.md        # Phase 1 artifact
├── contracts/           # Phase 1 API contracts
└── tasks.md             # Filled by /speckit.tasks
`

### Source Code (repository root)

`	ext
LiteDB/
├── Client/
├── Document/
├── Engine/
├── Plugins/
└── Utils/

LiteDB.Vector/
├── Engine/
├── Extensions/
├── Query/
└── Utils/

LiteDB.Tests/
├── Engine/
├── Plugins/
└── BsonValue/

LiteDB.Vector.Tests/
├── VectorIndex_Tests.cs
└── Integration/

scripts/
└── vector/
    └── Invoke-VectorUpgrade.ps1
`

**Structure Decision**: This work stays inside the existing LiteDB library/plugin layout; no new top-level projects are required. Production code updates land in LiteDB/ and LiteDB.Vector/, with mirrored coverage in LiteDB.Tests/ and LiteDB.Vector.Tests/. Repo hygiene tweaks live under scripts/vector/ and root .gitignore.

## Complexity Tracking

(No constitutional violations at this stage.)
