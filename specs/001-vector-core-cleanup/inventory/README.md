# Vector Component Inventory

## Overview

This directory aggregates all `VectorComponentRecord` documents for User Story&nbsp;1. Each record groups the LiteDB source files that currently expose or depend on vector search behaviour inside the core library. The inventory is sourced from `artifacts_temp/vector-cleanup/raw-search-results.txt` (196 matches ↦ 28 unique files) and normalized into component groupings for downstream migration planning.

## Component Index

| Component ID | Area | Files | Decision | Verification |
|--------------|------|-------|----------|--------------|
| `public-api-surface` | PublicApi | 6 | `pending-decision` | _(pending)_ |
| `query-planning-core` | QueryPlanning | 4 | `pending-decision` | _(pending)_ |
| `bson-serialization-surface` | Serialization | 7 | `pending-decision` | _(pending)_ |
| `storage-engine-vector` | StorageEngine | 9 | `pending-decision` | _(pending)_ |
| `service-infrastructure-factory` | ServiceInfrastructure | 2 | `pending-decision` | _(pending)_ |

## Source Artifacts

- Inventory records: `public-api.json`, `query-planning.json`, `bson-serialization.json`, `storage-engine.json`, `service-infrastructure.json`
- Search inputs: `artifacts_temp/vector-cleanup/raw-search-results.txt` and deduplicated `artifacts_temp/vector-cleanup/affected-files.txt`
- Schema & templates: `../inventory-schema.json`, `../templates/component-record.json`

## Next Steps

- Populate `decisionId` values once MigrationDecision files are authored (Phase&nbsp;4).
- Attach verification step identifiers after `specs/001-vector-core-cleanup/verification/*.json` is established.
