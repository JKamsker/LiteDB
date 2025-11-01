# LiteDB Release Notes Template

Use this template when drafting GitHub releases so spatial plugin guidance is captured alongside the usual highlights. Replace bracketed text before publishing.

## ✨ Headline Features
- [ ] Spatial plugin decouples from core – remind consumers that registering `new SpatialPlugin()` is required for spatial indexes, expressions, and LINQ. Link directly to the quickstart section on plugin registration.
- [ ] Other key features or fixes for this release.

## ⚠️ Breaking Changes / Required Actions
- [ ] Core package no longer ships spatial assemblies. Add `LiteDB.Spatial` to projects that rely on spatial queries.
- [ ] Call out any additional breaking changes introduced in this release.

## 🚚 Migration Steps
1. Add package references: `dotnet add package LiteDB` (new version) and `dotnet add package LiteDB.Spatial` (matching version).
2. Register the plugin in `LiteDatabase` construction.
3. Re-run `EnsureIndex` on spatial members so the interceptor provisions `_spatial_meta`, `_idx`, and `_mbb` (see [Spatial Guide](spatial-guide.md#2-provision-indexes-with-ensureindex)).
4. Replace legacy `Spatial.*` helpers with `WhereNear` / `WhereWithinBox` extensions or BsonExpression equivalents.
5. Verify results with the [Spatial Diagnostics guide](spatial-diagnostics.md) or the `samples/SpatialApiSample` project.

> Help readers find deeper instructions: link to [Quickstart](specs/001-spatial-plugin-migration/quickstart.md) and [Upgrade Guide](spatial-upgrade.md) in this section.

## 🧪 Validation Summary
- [ ] `dotnet test LiteDB.sln --settings tests.runsettings` (without spatial plugin)
- [ ] `dotnet test LiteDB.Spatial.Core.Tests`
- [ ] Benchmarks / stress suites compared against pre-migration baselines (attach numbers in release body)

## 📄 Additional Resources
- Quickstart: `specs/001-spatial-plugin-migration/quickstart.md`
- Usage guide: `docs/spatial-guide.md`
- Upgrade guide: `docs/spatial-upgrade.md`
- Diagnostics: `docs/spatial-diagnostics.md`
- Migration plan: `docs/spatial-plugin-migration-plan.md`

## ✅ Final Checks
- [ ] Release notes mention the plugin requirement and link to migration docs.
- [ ] NuGet package descriptions updated if spatial references were removed from core.
- [ ] GitHub release assets include the `LiteDB.Spatial.*` packages along with the core library.
