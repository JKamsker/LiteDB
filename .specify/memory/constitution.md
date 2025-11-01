<!--
Sync Impact Report:
- Version Change: 1.1.0 -> 1.2.0
- Added Principles:
  - VI. Plugin-First for Non-Core Features - New principle establishing plugin architecture
    for features like vector search, spatial indexing, and other domain-specific extensions
- Modified Sections:
  - Core Principles - Added Principle VI (renumbered from 5 to 6 total principles)
  - Code Quality Standards - Enhanced plugin organization guidance
- Rationale for MINOR bump: New principle added (plugin-first architecture)
- Templates Status:
  ✅ plan-template.md - Constitution Check section ready
  ✅ spec-template.md - Requirements alignment verified
  ✅ tasks-template.md - Task categorization aligned with principles
- Follow-up TODOs: None
-->

# LiteDB Constitution

## Core Principles

### I. Library-First Architecture

The `LiteDB/` library is the canonical source of all database functionality. Every feature MUST be implemented within the core library structure before any companion applications (Shell, Benchmarks, Tools) expose it.

- Core library MUST remain self-contained with clear domain separation: `Engine/`, `Document/`, `Client/`, `Utils/`
- New features MUST be placed in the appropriate domain directory
- Cross-domain dependencies MUST be minimized and explicitly justified
- Plugin architecture (framework code under `LiteDB/Plugins/` with concrete plugins shipped as sibling packages such as `LiteDB.Spatial/`) MUST maintain clear boundaries and not leak into core domains

**Rationale**: Maintains architectural integrity and ensures all functionality is independently testable without UI or CLI dependencies.

### II. Testing Discipline

All code changes MUST include appropriate test coverage using the established testing framework (xUnit + FluentAssertions). Tests are organized by feature area mirroring production structure.

- Test files MUST be placed in `LiteDB.Tests/` with folder structure mirroring `LiteDB/` domains
- Test class names MUST match the type under test with descriptive `[Fact]`/`[Theory]` method names
- All tests MUST complete within the 300-second timeout defined in `tests.runsettings`
- Integration tests, stress tests, and benchmarks are first-class artifacts maintained alongside unit tests
- Spatial features MUST include tests in `LiteDB.Spatial.Core.Tests/` following the same conventions
- **Each PR MUST include tests demonstrating the fix/feature works as intended**, except when:
  - Changes are purely documentation/comments
  - Changes are build/tooling configuration only
  - Testing is genuinely infeasible (must be explicitly justified in PR description)

**Rationale**: Comprehensive testing prevents regressions in storage internals and ensures reliability of the embedded database. PR-level test requirements ensure every change is verifiable and maintainable.

### III. API Documentation

All public APIs MUST be documented with XML documentation comments. The build process generates `LiteDB.xml` for consumer tooling.

- Public classes, methods, properties, and events MUST have `<summary>` documentation
- Complex methods MUST include `<param>` and `<returns>` documentation
- Breaking changes or deprecated APIs MUST be marked with `<remarks>` or `[Obsolete]`
- Internal/private members MAY be documented for maintainability but it is not required

**Rationale**: As an embedded library, consumers rely on IntelliSense and generated documentation for correct usage.

### IV. Performance-First Development

Performance and reliability are non-negotiable for an embedded database. All storage engine changes MUST be validated against performance benchmarks.

- `LiteDB.Benchmarks/` MUST be executed for changes to `Engine/`, indexing, or query processing
- `LiteDB.Stress/` MUST be executed for changes affecting concurrency, transactions, or recovery
- Performance regressions MUST be justified with detailed rationale in PR descriptions
- Unsafe code is permitted in `Engine/` components when justified with comments explaining the performance benefit

**Rationale**: LiteDB competes on speed, footprint, and reliability; degradation is unacceptable without explicit tradeoff documentation.

### V. Cross-Platform Compatibility

LiteDB targets both legacy (.NET Standard 2.0) and modern (.NET 8.0) frameworks. All code MUST compile and function correctly on both targets.

- New features MUST support both `netstandard2.0` and `net8.0` targets
- Nullable reference type warnings MUST NOT be introduced
- Platform-specific code MUST use conditional compilation (`#if`) with clear comments
- Dependencies MUST be compatible with both targets

**Rationale**: Broad compatibility ensures LiteDB remains viable across desktop, web, mobile, and IoT scenarios.

### VI. Plugin-First for Non-Core Features

Features that are NOT core to the embedded document database functionality MUST be implemented as plugins when architecturally feasible. Non-core features include domain-specific extensions like spatial indexing, vector search, full-text search, and specialized data types.

- Non-core features MUST ship as external plugin packages (e.g., `LiteDB.Spatial`, `LiteDB.VectorSearch`) that connect through the framework hosted under `LiteDB/Plugins/`
- The `LiteDB/Plugins/` directory contains shared plugin infrastructure only; feature implementations MUST reside outside the core library
- Plugins MUST NOT leak implementation details into core domains (`Engine/`, `Document/`, `Client/`, `Utils/`)
- Plugin interfaces MUST be stable and well-documented for third-party extensions
- The plugin system itself is in initial development phase; changes to plugin infrastructure are acceptable and encouraged to improve extensibility
- Plugin architecture evolution MUST maintain backward compatibility where possible, but breaking changes are acceptable during the initial phase with proper deprecation notices

**Core vs. Non-Core Classification**:
- **Core**: Document CRUD, indexing primitives, transactions, ACID guarantees, query engine, file storage, recovery, collection management
- **Non-Core**: Spatial/geographic queries, vector similarity search, full-text search, domain-specific data types, specialized aggregations

**Transition Note**: Legacy spatial components remain under `LiteDB/Spatial` while the migration in `docs/spatial-plugin-migration-plan.md` completes. Contributors MUST avoid expanding the legacy folder and implement new non-core features exclusively via plugin packages.

**Rationale**: Plugin architecture keeps the core library focused, maintainable, and lightweight while enabling rich ecosystems of specialized features. Users only pay (in binary size and complexity) for features they actually use.

## Code Quality Standards

### Coding Style

All C# code MUST follow the repository conventions defined in `.editorconfig` and maintained in `AGENTS.md`:

- Four-space indentation (no tabs)
- Allman brace style (opening brace on new line)
- Grouped `using` directives with System namespaces first
- Use `var` only when the right-hand type is obvious
- Meaningful variable and method names (avoid abbreviations except common ones: `db`, `id`, `col`)

### Code Organization

- Keep related functionality cohesive within domain folders
- Avoid circular dependencies between domains
- **Non-core features MUST be implemented as plugins** in `Plugins/` or separate packages (e.g., `LiteDB.Spatial.*/`)
- Plugin implementations MUST follow the established plugin pattern and maintain clear boundaries from core domains
- Helper utilities shared across domains belong in `Utils/`
- Cross-plugin utilities belong in shared plugin libraries (e.g., `LiteDB.Spatial.Core/`)

## Development Workflow

### Build & Test

- Restore dependencies: `dotnet restore`
- Build: `dotnet build LiteDB.sln -c Release`
- Test: `dotnet test LiteDB.sln --settings tests.runsettings`
- Focused test run: `dotnet test LiteDB.Tests --filter FullyQualifiedName~Engine`
- Package: `dotnet pack LiteDB/LiteDB.csproj -c Release`

### Commit Standards

- Concise, present-tense subject lines (e.g., "Add spatial index support")
- Reference issues inline when applicable (e.g., "Fix #123")
- Include before/after performance metrics when touching storage internals
- Keep commits focused on a single logical change

### Pull Request Requirements

Each PR MUST include:

- **Problem description**: What issue or feature is being addressed
- **Approach**: Technical solution summary
- **Test coverage**: New or modified tests demonstrating the fix/feature works correctly
  - Exception: Documentation-only or infeasible cases (must justify in PR description)
  - Tests should fail on the base branch and pass with the PR changes
- **Test results**: Output of `dotnet test` showing all tests passing
- **Performance impact**: Benchmark results if touching `Engine/`, indexing, or queries
- **Breaking changes**: Explicit callout if public API changes

## Governance

This constitution supersedes all other development practices and guides. All pull requests and code reviews MUST verify compliance with these principles.

### Amendment Process

1. Amendments MUST be proposed via pull request to `.specify/memory/constitution.md`
2. Amendment PR MUST include:
   - Sync Impact Report (prepended as HTML comment)
   - Version increment following semantic versioning:
     - **MAJOR**: Backward-incompatible principle removal or redefinition
     - **MINOR**: New principle or materially expanded guidance
     - **PATCH**: Clarifications, wording fixes, non-semantic refinements
   - Updates to affected templates (plan, spec, tasks) and command files
3. Amendments require approval from repository maintainers

### Compliance Review

- All feature planning (via `.specify/templates/plan-template.md`) MUST include a "Constitution Check" section
- Gate failures (principle violations) MUST be explicitly justified in the "Complexity Tracking" section
- Runtime development guidance is maintained in `AGENTS.md` (non-constitutional operational details)

### Living Document

This constitution is a living document. As new patterns emerge or architectural decisions change, this file MUST be updated to reflect current project realities. When in doubt, favor clarity and testability over brevity.

**Version**: 1.2.0 | **Ratified**: 2025-11-01 | **Last Amended**: 2025-11-01


