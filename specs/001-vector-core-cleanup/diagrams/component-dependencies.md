# Component Dependency Diagram

```mermaid
graph TD
    subgraph Decisions
        D1[move-to-plugin-short<br/>Owners: Vector Plugin Team]
        D2[requires-infrastructure<br/>Owners: Core Engine + Vector Plugin]
    end

    subgraph Gaps
        G1[gap-indexing-extensibility<br/>Impact: High]
        G2[gap-query-state<br/>Impact: Critical]
        G3[gap-bson-serialization<br/>Impact: Critical]
        G4[gap-storage-pipeline<br/>Impact: Critical]
    end

    C1[public-api-surface<br/>Area: PublicApi]
    C2[service-infrastructure-factory<br/>Area: ServiceInfrastructure]
    C3[query-planning-core<br/>Area: QueryPlanning]
    C4[bson-serialization-surface<br/>Area: Serialization]
    C5[storage-engine-vector<br/>Area: StorageEngine]

    C1 --> D1
    C1 --> G1

    C2 --> D1

    C3 --> D2
    C3 --> G2

    C4 --> D2
    C4 --> G3

    C5 --> D2
    C5 --> G4

    D1 -. guides .-> LiteDB.Vector
    D2 -. blocked by .-> InfrastructureUpgrades
```

**Legend**

- **Solid arrows** link inventory components to their governing migration decision or blocking gap.
- **Dashed arrows** highlight external dependencies: `LiteDB.Vector` implementation work and the shared infrastructure upgrade stream.
- Critical gaps (G2-G4) must be resolved before components `C3-C5` can migrate; `C1` and `C2` can advance in parallel once plugin shims land.
