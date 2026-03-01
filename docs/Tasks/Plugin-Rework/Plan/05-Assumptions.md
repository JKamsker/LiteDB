# Assumptions / Defaults

- Default missing-plugin behavior remains `PluginMissingBehavior.RefuseDatabase` (no breaking change).
- `$plugins` initially reports only plugin-owned indexes (not future plugin assets like arbitrary page allocations).
- Validation-on-open scans persisted collection metadata and must not depend on a newly introduced persisted header marker being present (legacy databases exist).
- Reuse-engine factory mode (`FactoryReuse.ReuseEngine`) shares a single engine/context pair and initializes plugins exactly once for that pair.
- Factory reuse assumes `ILitePlugin.Initialize` is registration-only and plugins do not capture the `LiteDatabase` instance they are initialized with.
