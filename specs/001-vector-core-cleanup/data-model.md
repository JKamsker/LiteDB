# Data Model

## VectorComponentRecord

- **Description**: Groups a set of core files that still contain vector-specific logic and need migration classification.
- **Fields**:
  - `area` (enum): `PublicApi`, `QueryPlanning`, `Serialization`, `StorageEngine`, `ServiceInfrastructure`.
  - `files` (array<string>): Absolute or project-relative paths under `LiteDB/`; must be non-empty and unique within the record.
  - `scopeSummary` (string): Narrative describing how the files relate to vector search behaviour.
  - `decision` (reference): Links to a `MigrationDecision` entry that captures the current plan.
  - `notes` (string, optional): Additional context, blockers, or sequencing guidance.
- **Relationships**:
  - `VectorComponentRecord.decisionId -> MigrationDecision.id`
  - `VectorComponentRecord.requiredGapIds -> PluginInfrastructureGap.id[]` (zero or more gaps that block migration).
- **Validation Rules**:
  - `files` must resolve under `LiteDB/` and reflect current repository structure.
  - Each record must reference exactly one `MigrationDecision`.
  - Records marked `MoveToPlugin` must list at least one verification step ensuring compatibility.

## MigrationDecision

- **Description**: Captures the current stance for a vector component (move now, blocked, or stay).
- **Fields**:
  - `id` (string/UUID): Stable identifier for cross-referencing.
  - `status` (enum): `MoveToPlugin`, `NeedsInfrastructure`, `RemainInCore`.
  - `prerequisites` (array<string>): Concrete actions required before executing the decision (e.g., “Add query state bag”).
  - `owners` (array<string>): Responsible teams or individuals (e.g., “Vector Plugin”, “Core Engine”).
  - `targetRelease` (string, optional): Planned milestone once prerequisites are met.
- **Relationships**:
  - Referenced by multiple `VectorComponentRecord` entries.
  - May reference one or more `PluginInfrastructureGap` items when `status == NeedsInfrastructure`.
- **Validation Rules**:
  - `status == MoveToPlugin` requires at least one prerequisite that ensures safe migration sequencing.
  - `status == RemainInCore` must include justification explaining why plugin migration is infeasible.
  - `owners` list must include both originating and receiving teams when migration crosses project boundaries.

## PluginInfrastructureGap

- **Description**: Defines missing extensibility points that prevent a migration decision from proceeding.
- **Fields**:
  - `id` (string/UUID): Stable identifier.
  - `category` (enum): `QueryState`, `BsonSerialization`, `StoragePipeline`, `Indexing`, `Diagnostics`.
  - `description` (string): Precise explanation of the missing capability.
  - `impact` (enum): `Critical`, `High`, `Medium`, `Low` based on how many records are blocked.
  - `workItem` (string, optional): Pointer to tracking issue or task once created.
- **Relationships**:
  - Linked from `VectorComponentRecord.requiredGapIds`.
  - May also be associated with roadmap epics outside this feature (tracked via `workItem`).
- **Validation Rules**:
  - `impact == Critical` requires at least one linked `VectorComponentRecord`.
  - Each gap should map to a single owner to avoid split accountability.
  - Gaps must document expected plugin API changes before implementation begins.

## VerificationStep

- **Description**: Enumerates the checks required to prove vector logic no longer leaks from the core after migration phases.
- **Fields**:
  - `id` (string/UUID)
  - `category` (enum): `Search`, `Build`, `Compatibility`, `Plugin`, `Regression`.
  - `command` (string): Exact command or procedure (e.g., `rg "Vector" LiteDB`).
  - `expectedOutcome` (string): Criteria for success (e.g., “No paths outside plugin namespace”).
  - `phase` (enum): `PreMigration`, `DuringMigration`, `PostMigration`.
- **Relationships**:
  - Multiple verification steps map to each `MigrationDecision` to ensure readiness before closure.
  - Steps reference affected `VectorComponentRecord` entries for traceability.
- **Validation Rules**:
  - `command` must be executable in CI (no interactive prompts).
  - Each `VectorComponentRecord` must have at least one verification step in `PostMigration`.

## Relationship Overview

- One `MigrationDecision` may serve many `VectorComponentRecord` entries.
- `PluginInfrastructureGap` entities unblock both decisions and verification steps; they serve as the linking mechanism to the plugin roadmap.
- `VerificationStep` provides the audit checklist per record and ensures compliance with FR-005 (verification strategy).
