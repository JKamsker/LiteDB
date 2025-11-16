# Research Log – Vector Plugin Isolation

## Decision 1: Diagnostics Should Reference Plugin ID
- **Decision**: User-facing diagnostics for missing vector functionality will explicitly mention the `LiteDB.Vector` plugin ID (e.g., `pluginId="LiteDB.Vector"`) while still going through the generic plugin-missing pathway.
- **Rationale**: Calling out the plugin ID shortens the support loop, matches current telemetry filters, and aligns with the spec requirement for actionable messages without forcing consumers to inspect exception data.
- **Alternatives Considered**:
  - *Generic wording only*: reduced clarity; users would still need documentation lookup.
  - *Custom exception types*: would reintroduce vector-specific code into core and violate the plugin boundary.

## Decision 2: Default Handling for Pre-release Vector Prototype Databases
- **Decision**: LiteDB will open databases created by the in-progress vector feature even when LiteDB.Vector is absent but will refuse operations that touch vector-dependent collections/documents. The engine will mark vector indexes as "unavailable" and surface diagnostics instructing users to install the plugin; non-vector data remains accessible. Note: these prototype files only exist in internal/dev environments today, but the policy prepares us for forward compatibility once the feature ships.
- **Rationale**: This approach preserves stability, maintains access to unaffected data, avoids unexpected failures for applications that only use subset of collections, and still enforces correctness by blocking unsafe operations.
- **Alternatives Considered**:
  - *Refuse to open the entire database*: overly restrictive for multi-tenant files where only a subset uses vector search.
  - *Allow all operations regardless of missing plugin*: risks corrupting metadata and violates the stability requirement.
