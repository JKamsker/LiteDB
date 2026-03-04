# Executive Summary – Vector Core Cleanup (2025-11-02)

## Snapshot

- Inventory sweep captured **196** vector references across **28** LiteDB core files, consolidated into **5** component records (`SUMMARY.md`).
- Migration decisions split into two tracks: **MoveToPlugin (2)** covering Public API and Service Infrastructure, and **NeedsInfrastructure (3)** covering Query Planning, Serialization, and Storage Engine.
- Four infrastructure gaps remain open (3 Critical, 1 High); each component has explicit prerequisites and verification coverage.
- Full verification suite executed (`dotnet test LiteDB.sln --settings tests.runsettings`, `dotnet test LiteDB.Vector.Tests`) with success; JSON/schema validations confirm artifacts are consistent.

## Key Outcomes

- Clear separation between plugin-ready work (EnsureVectorIndex shims, service factory relocation) and infrastructure-dependent migrations.
- Dependency diagram (`diagrams/component-dependencies.md`) maps component-to-decision-to-gap relationships for planning.
- Research updated with final synthesis and dual-track strategy; AGENTS.md documents the ongoing process for contributors.

## Outstanding Gaps (Owners: Core Engine + Vector Plugin)

| Gap ID | Severity | Description | Unblocked Components |
|--------|----------|-------------|----------------------|
| `gap-query-state` | Critical | Introduce plugin-managed query metadata bag to remove vector fields from `Query`. | `query-planning-core` |
| `gap-bson-serialization` | Critical | Provide BSON type registration so plugin owns `BsonType.Vector` semantics. | `bson-serialization-surface` |
| `gap-storage-pipeline` | Critical | Enable plugin page factories/metadata for vector index pages and rebuild paths. | `storage-engine-vector` |
| `gap-indexing-extensibility` | High | Expand index strategy registration beyond `IIndexInterceptorRegistry`. | `public-api-surface` (also part of MoveToPlugin track) |

## Immediate Next Steps

1. **Plugin Team**  
   - Implement EnsureVectorIndex extension wrapper and index strategy registration to satisfy `move-to-plugin-short` prerequisites.  
   - Host `VectorIndexServiceFactory` inside `LiteDB.Vector`, replacing `InternalsVisibleTo` dependencies.  
   - Pair with Core Engine to prototype query metadata bag and BSON registry APIs (required for critical gaps).

2. **Core Engine Team**  
   - Review and approve plugin extension hooks (query state bag, BSON registry, storage page factory) ensuring backward compatibility.  
   - Provide migration toggles and transitional shims so legacy projects continue to load vector indexes during rollout.  
   - Validate updated extensibility points against `verification/*.json` commands in CI.

3. **Joint Actions**  
   - Track progress using gap JSON `status` fields; move entries to `Resolved` with work item references once merged.  
   - Maintain alignment via `migration/priority-*.md` plans; update decision `prerequisites` as blockers close.  
   - Schedule post-migration `rg "Vector" LiteDB` audit to confirm core cleanup before plugin-only delivery.

## Handoff Checklist

- Documentation artifacts are current (`SUMMARY.md`, `EXECUTIVE-SUMMARY.md`, `diagrams/`, `research.md`).  
- Validation scripts for JSON, file paths, and cross references executed with no blocking issues (only contingency decision unused).  
- Tests passed with existing nullable and net461 warnings acknowledged; no new failures introduced.

Stakeholders can now begin implementation in the two parallel streams while infrastructure upgrades advance. Update this summary as gaps close or new decisions emerge.
