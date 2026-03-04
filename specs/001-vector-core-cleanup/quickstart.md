# Quickstart

Follow these steps to refresh the vector inventory and update migration guidance while keeping the core library free of non-core vector features.

## 1. Prepare the environment

1. Checkout the feature branch:
   ```pwsh
   git checkout 001-vector-core-cleanup
   ```
2. Restore and build the solution to ensure the baseline compiles:
   ```pwsh
   dotnet restore
   dotnet build LiteDB.sln -c Release
   ```

## 2. Regenerate the vector inventory

1. Run the canonical search to enumerate remaining vector references:
   ```pwsh
   rg "Vector" LiteDB > artifacts_temp/vector-inventory.txt
   ```
2. Cross-check the output against `specs/001-vector-core-cleanup/spec.md` and update any new paths in the `Vector Code Inventory` table.
3. For each new or changed path, create/update a `VectorComponentRecord` entry in the planning tooling (see API contract) or in the working spreadsheet.

## 3. Update migration decisions

1. Review the existing `MigrationDecision` objects and ensure prerequisites reflect current blockers (use `specs/001-vector-core-cleanup/data-model.md` for field definitions).
2. For component records ready to move, record the target plugin PR/issue and switch their decision status to `MoveToPlugin` using the `/vector/components/{id}` PATCH contract.
3. If a change uncovers new infrastructure gaps, create a `PluginInfrastructureGap` entry via `/vector/gaps` and link affected component IDs.

## 4. Verify plugin readiness

1. Execute the verification steps tied to the component (`rg`, targeted `dotnet test`, or plugin-specific regression scripts).
2. Confirm LiteDB still passes the full suite:
   ```pwsh
   dotnet test LiteDB.sln --settings tests.runsettings
   dotnet test LiteDB.Vector.Tests
   ```
3. Unload `LiteDB.Vector` from the consumer application and confirm the core surfaces a clear error when invoking vector APIs.

## 5. Document the outcome

1. Update `research.md` with any new learnings (e.g., performance measurements or infrastructure decisions).
2. Record migration sequencing changes in the `Migration Priorities` section of the spec.
3. Share the updated inventory with the plugin and core teams before scheduling migrations.
