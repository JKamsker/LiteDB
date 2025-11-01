# Baselines – Spatial Plugin Migration

## 2025-11-01 – Pre-migration Reference

- `dotnet build LiteDB.Benchmarks/LiteDB.Benchmarks.csproj -c Release`
  - Output size: `LiteDB.dll` 622.50 KB, `LiteDB.Spatial.dll` 29.00 KB
  - Warnings: nullable annotations (CS8632), crypto deprecation (SYSLIB0041), rethrow (CA2200), legacy query API (CS0618)
- `dotnet build LiteDB.Stress/LiteDB.Stress.csproj -c Release`
  - Output size: `LiteDB.dll` 622.50 KB

These measurements act as the baseline for post-migration performance and package size comparisons.

## 2025-11-01 – Post-core spatial removal

- `dotnet build LiteDB.sln -c Release`
  - Output size: `LiteDB.dll` 591.00 KB (net8.0 target)
  - Warnings: third-party net461 support (System.Text.Json stack), nullable annotations, analyzer suggestions (existing)
  - Notes: Spatial assemblies no longer produced from core build
