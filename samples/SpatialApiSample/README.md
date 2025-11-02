# Spatial API Sample

This sample project demonstrates the spatial plugin flow end-to-end. Use it alongside the enablement checklist to verify an environment before rolling the plugin into a product build.

## How to Run

```bash
dotnet run --project samples/SpatialApiSample
```

Once the app is running:

1. `POST /seed` seeds points of interest, registers `SpatialPlugin`, and calls `EnsureIndex` on the annotated geometry member.
2. `GET /places/near?lat=<lat>&lon=<lon>&radiusKm=5` executes a `WhereNear` query through the plugin’s LINQ extensions.
3. `GET /places/within?minLat=<minLat>&minLon=<minLon>&maxLat=<maxLat>&maxLon=<maxLon>` exercises the bounding-box pipeline.

## Checklists & Diagnostics

- Follow the [spatial plugin enablement checklist](../../docs/spatial-plugin-enable-checklist.md) to validate configuration.
- Review the [spatial quickstart](../../specs/001-spatial-plugin-migration/quickstart.md) for package alignment and setup steps.
- Capture planning snapshots using the helpers in [spatial-diagnostics.md](../../docs/spatial-diagnostics.md) if queries behave unexpectedly.
