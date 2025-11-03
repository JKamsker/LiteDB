# Vector Core Cleanup - Final Inventory Summary

## Inventory Snapshot

- Vector references scanned via `rg "Vector" LiteDB` on 2025-11-04: **205** matches confined to plugin contracts, guard rails (`BufferSliceExtensions`), and shared abstractions. No engine runtime classes remain in the core assembly.
- Components tracked in the inventory: **0 outstanding** (Public API, Query Planning, Serialization, Storage Engine, and Service Infrastructure have all been relocated to `LiteDB.Vector`).
- Migration decision mix: **2** `MoveToPlugin`, **0** `NeedsInfrastructure`, **0** `RemainInCore` – all actions completed by the plugin migration.
- Open infrastructure gaps: **0** – query metadata, BSON type registration, page factory hooks, and service dispatch are now satisfied by the plugin registries.

## Component Coverage

| Area | Status | Notes |
|------|--------|-------|
| PublicApi | ✅ Moved to LiteDB.Vector | EnsureVectorIndex shims route through the vector strategy registry; no vector-aware APIs remain in `LiteDB`. |
| ServiceInfrastructure | ✅ Moved to LiteDB.Vector | `VectorIndexServiceFactory` and metadata helpers now live in the plugin package; core only retains the plugin hook. |
| QueryPlanning | ✅ Plugin-owned | Query metadata bag and planning rule implementations reside under `LiteDB.Vector`. |
| Serialization | ✅ Plugin-owned | Vector BSON handling is delegated to the plugin registry with core guard rails rejecting vector payloads without the plugin. |
| StorageEngine | ✅ Plugin-owned | Vector pages, nodes, and metadata structures execute solely in `LiteDB.Vector` via the page factory registry. |

## Migration Decision Status

- `move-to-plugin-short` – Complete. Public API surface and service factory components are hosted by the vector plugin.
- `requires-infrastructure` – Closed. Query metadata, serialization, and storage gaps are resolved by the new plugin registries.
- `remain-in-core` – Not applicable; no vector components require retention in the core assembly.

## Infrastructure Gap Outlook

All previously tracked gaps (`gap-indexing-extensibility`, `gap-query-state`, `gap-bson-serialization`, `gap-storage-pipeline`) are closed. No further core work is required for vector runtime ownership.

## Verification Coverage

- Build: `dotnet build LiteDB.Vector/LiteDB.Vector.csproj -c Release` (warning-free after plugin suppressions).
- Core Guard Rails: `dotnet build LiteDB.sln -c Release` (confirms vector operations fail fast without the plugin).
- Post-migration Search Sweep: `rg "Vector" LiteDB` – validates remaining references are limited to plugin integration points.

Vector runtime responsibilities now reside entirely in `LiteDB.Vector`, leaving the core ready for release without vector-specific internals.
