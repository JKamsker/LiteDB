# Feature Specification: Spatial Plugin Migration

**Feature Branch**: `001-spatial-plugin-migration`  
**Created**: 2025-11-01  
**Status**: Draft  
**Input**: User description: "The removal of the in-core spatial features in favor of the new plugin architecture based spatial features. (see spatial migration plan)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Core build without spatial baggage (Priority: P1)

Repository maintainers need the LiteDB core solution to build, test, and publish without any spatial assemblies linked directly, while exposing plugin hooks that spatial functionality can use.

**Why this priority**: Removing spatial code from core is the primary goal; until the core is decoupled, plugin work cannot ship.

**Independent Test**: Run `dotnet build LiteDB.sln -c Release` and `dotnet test LiteDB.sln --settings tests.runsettings` without referencing the spatial plugin; both must succeed and the produced packages must exclude spatial binaries.

**Acceptance Scenarios**:

1. **Given** a clean checkout of LiteDB with no spatial plugin referenced, **When** the solution is built, **Then** no project under `LiteDB/Spatial` compiles as part of the core output and the build succeeds.
2. **Given** unit tests that previously covered spatial code paths, **When** the spatial plugin is not enabled, **Then** tests either skip the spatial suites or assert a clear error indicating the plugin is required.

---

### User Story 2 - Opt-in spatial plugin restoration (Priority: P2)

Integrators who rely on spatial capabilities need to add the spatial plugin package and receive feature parity (LINQ support, expressions, indexes) through the new plugin extension points, without relying on bespoke configuration helpers.

**Why this priority**: Users must have an easy migration path so removing spatial from core does not break their workloads.

**Independent Test**: Create an application that references the new spatial plugin and run spatial LINQ queries, expression-based queries, and index operations; all should succeed and return results equivalent to the pre-migration behavior.

**Acceptance Scenarios**:

1. **Given** a database configured with the spatial plugin, **When** a caller invokes `collection.EnsureIndex(x => x.Location)` for a property typed as `GeoPoint`, **Then** the plugin intercepts the call and provisions the appropriate spatial index without extra helpers.
2. **Given** a queryable collection with the spatial plugin installed, **When** `WhereNear` (or the equivalent plugin-supplied LINQ extension) is used, **Then** the plugin translates the call into the spatial plan and returns the expected nearby points while `SpatialExpressions.Near` remains available for advanced scenarios.

---

### User Story 3 - Release readiness documentation for first adopters (Priority: P3)

Internal product teams preparing the first public release need authoritative documentation that explains how to enable the spatial plugin, outlines supported scenarios, and highlights behavioral differences from previous prototypes.

**Why this priority**: Even without external consumers, internal launch teams require clear guidance to avoid misconfiguration and support inquiries when the plugin ships.

**Independent Test**: Follow the drafted enablement guide in a clean sample project; by applying the documented steps, the project compiles, plugin initialization succeeds, and spatial queries return expected results.

**Acceptance Scenarios**:

1. **Given** the enablement guide, **When** a developer adds the spatial plugin to a fresh LiteDB application, **Then** they can configure the plugin and execute a sample spatial query end-to-end without additional support.
2. **Given** the same guide, **When** support engineers review the documented limitations, **Then** they can identify unsupported scenarios and escalation paths prior to the first release.

---

### Edge Cases

- What happens when a consumer executes a spatial expression without enabling the plugin?  
  - The query planner must surface a descriptive error (`LiteException`) instructing users to add the spatial plugin, and core should not crash.
- How does the system handle databases that still contain legacy spatial metadata after core removal?  
  - Although spatial has not shipped publicly, internal databases created during development must remain readable; core should tolerate the metadata and the plugin should interpret or migrate it when present.
- What happens when both vector and spatial plugins register planning rules for the same query?  
  - Planning rule ordering must be deterministic; documentation should clarify priority configuration or conflict resolution.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `LiteDB` core project MUST remove all spatial namespaces, types, and expression helpers while still compiling and passing existing non-spatial tests.
- **FR-002**: The plugin context MUST expose factory-based LINQ resolver registration and enriched query planning hooks that spatial (and other) plugins can consume.
- **FR-003**: The spatial plugin MUST re-register all required expression functions, LINQ resolvers, and index strategies so that spatial queries behave identically to pre-migration releases.
- **FR-004**: When spatial APIs are invoked without the plugin, the system MUST emit a clear, actionable error message and avoid undefined behavior.
- **FR-005**: The core/plugin infrastructure MUST allow plugins to register custom index interceptors so that `EnsureIndex` on spatial types automatically delegates to spatial indexing logic.
- **FR-006**: The spatial plugin MUST provide `ILiteQueryable` extension methods (e.g., `WhereNear`) that wrap the underlying expressions to avoid namespace collisions and deliver ergonomic querying, while keeping `SpatialExpressions` available for advanced use.
- **FR-007**: Documentation MUST instruct future consumers how to add the spatial plugin, enable it in `LiteDatabase` construction, and understand behavior changes once the feature ships.
- **FR-008**: Automated test suites MUST cover both the plugin-disabled path (verifying graceful failures) and the plugin-enabled path (verifying functional parity).
- **FR-009**: Build and packaging pipelines MUST exclude spatial assemblies from core artifacts while producing plugin-specific packages for distribution.

### Key Entities *(include if feature involves data)*

- **Plugin Extension Points**: Represents the registries (expressions, indexes, LINQ resolvers, query planning rules) that plugins use to contribute features during `LiteDatabase` initialization.
- **Spatial Plugin Package**: Delivers spatial functionality as an `ILitePlugin` implementation, housing geometry types, metadata stores, and registration logic.
- **Migration Documentation**: The published guidance that enumerates upgrade steps, compatibility notes, and known limitations for consumers moving to the plugin model.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Core automated tests complete successfully with zero spatial assemblies referenced when the spatial plugin is not enabled.
- **SC-002**: Spatial plugin end-to-end tests demonstrate parity by matching legacy spatial query outputs within ±0.1% variance across representative datasets.
- **SC-003**: Enablement documentation enables at least 90% of internal pilot projects to integrate the spatial plugin without build failures attributable to spatial dependencies.
- **SC-004**: Release artifacts show a reduction of at least 5 MB in the core NuGet package size while publishing a separate spatial plugin package with complete release notes.
