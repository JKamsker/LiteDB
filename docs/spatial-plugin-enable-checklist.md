# Spatial Plugin Enablement Checklist

Use this checklist to guide internal teams through turning on the spatial plugin after the core/library separation. Each item links to the detailed guidance that already lives in this repository.

## 1. Prerequisites
- [ ] Confirm the application references aligned versions of `LiteDB` and `LiteDB.Spatial` (`spatial-upgrade.md` – _Package Alignment_).
- [ ] Ensure the target project enables plugin loading through the `LiteDatabase` constructor (`../specs/001-spatial-plugin-migration/quickstart.md#3-enable-the-spatial-plugin`).

## 2. Model Preparation
- [ ] Annotate spatial members or register mapper options so the interceptor can provision descriptors (`spatial-guide.md#1-enable-the-plugin-and-declare-options`).
- [ ] Review legacy `_spatial_meta` state and map any custom domains called out in the [migration guide](spatial-upgrade.md).

## 3. Index Provisioning
- [ ] Call `EnsureIndex` on each spatial member so the interceptor can hydrate metadata/interceptors (`spatial-guide.md#2-provision-indexes-with-ensureindex`).
- [ ] Capture a diagnostic snapshot with [`SpatialDiagnostics`](spatial-diagnostics.md) to verify the plugin registered rules and index strategies.

## 4. Query Verification
- [ ] Exercise the LINQ helpers (`WhereNear`, `WhereWithinBox`, etc.) in a smoke test or sample based on `../samples/SpatialApiSample` (`../specs/001-spatial-plugin-migration/quickstart.md#5-query-with-wherenear`).
- [ ] Run the spatial regression suite (`dotnet test LiteDB.Spatial.Core.Tests`) to confirm plugin-enabled coverage.

## 5. Operational Sign-off
- [ ] Update internal runbooks or README sections to reference this checklist and the spatial quickstart.
- [ ] Record benchmark deltas using the workflow outlined in [spatial-benchmarks.md](spatial-benchmarks.md) before shipping.

> **Tip**: The `../samples/SpatialApiSample` project is the recommended starting point when onboarding a new team—clone it and follow the quickstart to validate the checklist end-to-end.
