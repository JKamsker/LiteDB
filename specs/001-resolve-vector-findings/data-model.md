# Data Model

## QueryMetadataBag

- **Description**: Plugin-managed container that stores vector-specific planner inputs during command compilation and execution.
- **Fields**:
  - `pluginId` (string): Identifier for the registering plugin (e.g., `LiteDB.Vector`).
  - `keys` (array<string>): Known metadata keys reserved by the plugin such as `VectorField`, `VectorMetric`, `TargetEmbedding`.
  - `state` (map<string, object>): Arbitrary values persisted for the duration of the query pipeline.
  - `version` (int): Schema version to coordinate upgrades between core and plugin.
- **Relationships**:
  - Owned by `PluginQueryContext`.
  - Passed to registered index strategies during plan evaluation.
- **Validation Rules**:
  - `pluginId` must match a registered plugin descriptor.
  - `state` keys must exist in `keys`; unknown entries are rejected to keep contracts predictable.
  - `version` increments require migration logic supplied by the plugin.

## CustomBsonTypeDescriptor

- **Description**: Declares a BSON type reserved for plugin-owned serialization along with synchronous delegates for size calculation, encode/decode, and JSON formatting.
- **Fields**:
  - `pluginId` (string): Identifier for the owning plugin (e.g., `LiteDB.Vector`).
  - `typeCode` (byte): Unique code reserved from the plugin registry (plugin codes must be `>= 128`).
  - `name` (string): Human-readable identifier (`Vector`).
  - `calculateSize` (`Func<BsonValue, int>`): Computes serialized size.
  - `serializer` (`Action<object, BsonValue>`): Writes a value into a `BsonValue` buffer.
  - `deserializer` (`Func<object, BsonValue>`): Reads a value from a `BsonValue` buffer.
  - `jsonFormatter` (`Func<BsonValue, string>`): Converts serialized values to a JSON string for diagnostics/export.
  - `legacyAliases` (array<byte>): Optional list of legacy codes for backward compatibility.
- **Relationships**:
  - Registered through `ICustomBsonTypeRegistry`.
  - Referenced by upgrade scripts to migrate legacy data.
- **Validation Rules**:
  - `typeCode` must be unique across all registrations.
  - `serializer`/`deserializer` must round-trip values under regression tests.
  - `legacyAliases` require explicit upgrade handlers before activation.

## PageFactoryRegistration

- **Description**: Represents plugin-provided constructors for custom pages backed by deterministic numeric codes.
- **Fields**:
  - `pluginId` (string): Identifier for the owning plugin (e.g., `LiteDB.Vector`).
  - `pageType` (string): Logical classification (e.g., `VectorIndex`).
  - `numericCode` (byte): Persisted page type code reserved by the plugin.
  - `compatibilityRange` (string): Supported format versions (e.g., `>=8.0`).
  - `factory` (Func<PageConstructionContext, object>): Page creator delegate (typically returns a `BasePage`).
- **Relationships**:
  - Registered with the core `IPageTypeRegistry`/page factory registry.
  - Consumed by vector index strategies during index maintenance.
- **Validation Rules**:
  - `factory` must only produce pages deriving from `BasePage`.
  - `compatibilityRange` must include the current engine version before activation.
  - Registrations must not collide on numeric code or logical name.

## CustomIndexStrategyDescriptor

- **Description**: Aggregates plugin callbacks that implement vector indexing semantics end-to-end.
- **Fields**:
  - `pluginId` (string): Identifier for the owning plugin.
  - `strategyId` (string): Unique identifier referenced by public APIs.
  - `ensureIndex` (Func<CustomIndexEnsureContext, bool>): Handles index creation/upgrades.
  - `queryPlanner` (Action<CustomIndexQueryPlannerContext>): Injects vector operations into query planning.
  - `rebuildStrategy` (Action<CustomIndexRebuildContext>): Optional hook to participate in rebuild.
  - `requiredBsonTypes` (array<byte>): Type codes this strategy depends on.
  - `requiredPageTypes` (array<string>): Logical plugin page types this strategy depends on.
- **Relationships**:
  - Registered through `ICustomIndexStrategyRegistry`.
  - Consumes `QueryMetadataBag`, `PageFactoryRegistration`, and `CustomBsonTypeDescriptor`.
- **Validation Rules**:
  - `ensureIndex` must validate plugin availability and throw deterministic errors if missing prerequisites.
  - `queryPlanner` must not mutate core state outside metadata bag contracts.
  - `requiredBsonTypes` must be registered before strategy activation.

## UpgradeManifest

- **Description**: Captures ordered upgrade steps that transition existing databases to the plugin-managed vector implementation.
- **Fields**:
  - `manifestId` (string): Identifier referenced in documentation.
  - `steps` (array<UpgradeStep>): Ordered list of script references and validation commands.
  - `supportsInPlace` (bool): Indicates whether upgrades can occur without full export/import.
  - `rollbackPlan` (string): Narrative describing how to revert if validation fails.
- **Relationships**:
  - Referenced by verification docs under `specs/001-vector-core-cleanup/verification`.
  - Consumed by quickstart/test scripts to drive automation.
- **Validation Rules**:
  - `steps` must include at least one verification command per success criterion.
  - `rollbackPlan` must reference concrete scripts or documented procedures.



