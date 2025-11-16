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

## Decision 3: Pre-release to GA Migration Strategy
- **Decision**: Pre-release vector databases require explicit migration before GA plugin use. The migration path is: (1) use the final pre-release build to drop existing vector indexes (read-only access to old format for drop operations only), (2) upgrade to GA release with LiteDB.Vector plugin installed, (3) recreate indexes using the GA plugin. Alternatively, perform a logical export/import (recommended). GA releases will NOT attempt to upgrade pre-release formats automatically.
- **Rationale**: Pre-release formats are development artifacts not intended for production; automatic upgrades add complexity and risk. Explicit migration ensures users understand the breaking change and provides a clean cutover point. The pre-release build can READ old metadata minimally (enough to identify and drop indexes) but does not UPGRADE it in place.
- **Alternatives Considered**:
  - *Automatic format upgrade*: adds complexity, risks migration bugs, and doesn't clearly communicate breaking changes to users.
  - *Block all access to prerelease databases*: prevents graceful migration path and forces users to restore from backups.
- **Migration Support**: Pre-release builds retain limited read capability for metadata identification and drop operations. GA builds emit clear `LITE2002` diagnostics with migration guidance when encountering pre-release artifacts.

## Decision 4: Reserved Identifier Ranges for LiteDB.Vector
- **Decision**: LiteDB.Vector reserves the following identifier ranges:
  - **BSON Type Code**: `0x90` (single code, expandable to `0x90-0x9F` range if future vector BSON types needed)
  - **Page Type Codes**: `0xE0-0xE3` (currently using `0xE0` for `VectorIndexPage`, reserving 3 additional codes for future vector page types)
  - **IndexKind**: `"vector.hnsw"` (string identifier, extensible with `"vector.ivf"`, `"vector.pq"`, etc.)
- **Rationale**:
  - `0x90-0x9F` range chosen for BSON codes because it's in the "extended types" area above standard BSON spec codes (`0x00-0x13`) and below the max byte value, leaving room for other plugins.
  - `0xE0-0xEF` range for page types places plugin pages at the high end of the byte range, clearly separated from core page types (`0x00-0xDF`), with 16 codes available for all plugins (LiteDB.Vector claims first 4).
  - String-based `IndexKind` allows unlimited extensibility without numeric conflicts.
- **Alternatives Considered**:
  - *Start at 0x80*: Too close to potential future core BSON types; 0x90+ provides clearer separation.
  - *Use single codes per plugin*: Inflexible; LiteDB.Vector may need multiple page types (index pages, metadata pages, temp pages).
  - *Random/dynamic allocation*: Makes file format non-deterministic; fixed ranges ensure stable format.
- **Conflict Detection**: All registries validate codes during plugin initialization and throw `InvalidOperationException` with plugin ID and conflicting code if overlap detected (see T005a).

## Decision 5: Registry Conflict Resolution Strategy
- **Decision**: Plugin registries use "first registration wins" semantics with strict validation. When a plugin attempts to register a code/identifier already claimed by another plugin, the registry throws `InvalidOperationException` containing both plugin IDs and the conflicting value. Registration order is deterministic (plugins loaded in order specified in `LiteDatabaseOptions.Plugins` array).
- **Rationale**: Explicit failure prevents silent conflicts and data corruption. Deterministic load order ensures reproducible behavior. Clear error messages enable users to identify and resolve conflicts immediately.
- **Alternatives Considered**:
  - *Last registration wins*: Silent overwrites risk breaking previously-loaded plugins.
  - *Allow overlaps with runtime resolution*: Ambiguous behavior; makes file format interpretation non-deterministic.
  - *Namespace prefixing (e.g., plugin.code)*: Complicates wire format; byte codes are already scarce.
- **Implementation**: Each registry maintains a `Dictionary<TKey, Registration>` and validates on insert. Errors include diagnostic properties: `ConflictingPlugins`, `ConflictType`, `ConflictValue`.
