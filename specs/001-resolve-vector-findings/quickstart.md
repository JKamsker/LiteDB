# Quickstart

Follow this runbook to exercise the fully plugin-managed vector implementation and confirm the follow-up work resolves every `gap-*` item from `specs/001-vector-core-cleanup`.

## 1. Prepare the environment

1. Checkout the branch:
   ```pwsh
   git checkout 001-resolve-vector-findings
   ```
2. Restore and build the solution:
   ```pwsh
   dotnet restore
   dotnet build LiteDB.sln -c Release
   ```

## 2. Register plugin extensibility contracts

1. Start the local tooling service (see `specs/001-vector-core-cleanup/contracts/openapi.yaml` for context) or mock the endpoints.
2. Register the query metadata bag reserved keys:
   ```pwsh
   curl -X POST https://extensions.litedb.dev/api/plugins/LiteDB.Vector/registrations/query-metadata `
     -H 'Content-Type: application/json' `
     -d '{"pluginId":"LiteDB.Vector","version":1,"reservedKeys":["VectorField","VectorMetric","TargetEmbedding"],"description":"Vector search planner metadata"}'
   ```
3. Reserve the BSON type and page factory descriptors using the payloads defined in `contracts/plugin-extensibility.yaml`.
4. Submit the vector index strategy registration to replace `EnsureVectorIndex` shims.

## 3. Run migrations against a legacy database

1. Execute upgrade tooling (PowerShell or CLI) that consumes the UpgradeManifest entries:
   ```pwsh
   pwsh scripts/vector/Invoke-VectorUpgrade.ps1 -DatabasePath .\artifacts_temp\legacy-vector.db
   ```
2. Inspect the generated manifest report and verify `supportsInPlace` matches expected deployment modes.
3. Re-run the verification checklist:
   ```pwsh
   dotnet test LiteDB.sln --settings tests.runsettings
   dotnet test LiteDB.Vector.Tests
   rg "Vector" LiteDB
   ```

## 4. Validate observability and failure modes

1. Temporarily remove the plugin assembly from the probing path and execute a vector query—confirm the structured error cites remediation steps.
2. Reintroduce the plugin but register an outdated manifest; verify telemetry reports the incompatibility within a deployment cycle.
3. Capture logs/metrics referenced in the success criteria and archive them under `artifacts_temp/vector-followup`.

## 5. Document outcomes

1. Update `SUMMARY.md` in `specs/001-vector-core-cleanup` to mark all vector components resolved.
2. Append benchmark comparisons and telemetry snippets to `specs/001-resolve-vector-findings/research.md`.
3. Share the upgrade manifest and quickstart transcript with the plugin and core maintainers for release preparation.
