# Progress - Vector Plugin Extraction

## 2025-11-16
- Completed **T001** (`docs/vector-plugin-isolation.md`). Added the migration guide summarizing plugin optionality, prerelease migration paths, missing-plugin diagnostics, CI/test expectations, and troubleshooting so users understand how to install LiteDB.Vector and migrate existing databases.
- Completed **T002** (`scripts/verify-vector-clean.ps1`). Implemented the ripgrep-based guard that scans LiteDB/ for "Vector" references, supports repo-relative allowlists, and exits with an error when vector strings remain outside approved extension points.
