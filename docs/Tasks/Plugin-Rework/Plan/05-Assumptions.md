# Assumptions / Defaults

- Default missing-plugin behavior remains `PluginMissingBehavior.RefuseDatabase` (no breaking change).
- `$plugins` initially reports only plugin-owned indexes (not future plugin assets like arbitrary page allocations).
- Header marker is sticky and used as an optimization; it does not need to be cleared automatically.
- Shared-engine factory mode shares a single plugin context and initializes plugins exactly once for that context/engine pair.

