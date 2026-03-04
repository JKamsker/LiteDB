# LiteDB Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-11-01

## Active Technologies
- [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION] + [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION] (001-vector-plugin-migration)
- [if applicable, e.g., PostgreSQL, CoreData, files or N/A] (001-vector-plugin-migration)
- C# (`<LangVersion>latest</LangVersion>` targeting `netstandard2.0` + `net8.0`) + LiteDB core library, emerging plugin infrastructure under `LiteDB/Plugins`, `LiteDB.Vector` package for the destination implementation (001-vector-core-cleanup)
- LiteDB document store files (`*.db`) with BSON serialization and custom page formats (001-vector-core-cleanup)
- C# targeting `netstandard2.0` and `net8.0` multi-targeted libraries (`<LangVersion>latest</LangVersion>`) + LiteDB core library (`LiteDB/`), LiteDB plugin infrastructure under `LiteDB/Plugins/`, LiteDB.Vector plugin project, xUnit + FluentAssertions for validation (001-resolve-vector-findings)
- LiteDB file-based document store with BSON serialization, custom page formats, and vector index metadata persisted in data pages (001-resolve-vector-findings)
- C# 12 targeting .NET Standard 2.0 & .NET 8.0 + LiteDB core library, LiteDB.Plugins infrastructure, LiteDB.Vector plugin (optional) (001-vector-plugin-extraction)
- LiteDB file storage (BSON pages, vector metadata persisted as plugin-managed blobs) (001-vector-plugin-extraction)

- C# targeting .NET Standard 2.0 and .NET 8.0 + LiteDB core, LiteDB plugin framework, LiteDB.Spatial.* packages, xUnit + FluentAssertions (001-spatial-plugin-migration)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# targeting .NET Standard 2.0 and .NET 8.0

## Code Style

C# targeting .NET Standard 2.0 and .NET 8.0: Follow standard conventions

## Recent Changes
- 001-vector-plugin-extraction: Added C# 12 targeting .NET Standard 2.0 & .NET 8.0 + LiteDB core library, LiteDB.Plugins infrastructure, LiteDB.Vector plugin (optional)
- 001-resolve-vector-findings: Added C# targeting `netstandard2.0` and `net8.0` multi-targeted libraries (`<LangVersion>latest</LangVersion>`) + LiteDB core library (`LiteDB/`), LiteDB plugin infrastructure under `LiteDB/Plugins/`, LiteDB.Vector plugin project, xUnit + FluentAssertions for validation
- 001-vector-core-cleanup: Added C# (`<LangVersion>latest</LangVersion>` targeting `netstandard2.0` + `net8.0`) + LiteDB core library, emerging plugin infrastructure under `LiteDB/Plugins`, `LiteDB.Vector` package for the destination implementation


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
