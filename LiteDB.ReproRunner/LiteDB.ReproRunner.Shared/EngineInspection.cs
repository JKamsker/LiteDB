using System.Reflection;

namespace LiteDB.ReproRunner.Shared;

/// <summary>Locates the storage engine in both package and pooled source builds.</summary>
public static class EngineInspection
{
    public static IDisposable? EnterDirectContext(object? engine)
    {
        var context = engine?.GetType().GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(engine);
        if (context == null) return null; // Historical raw engines have no connection wrapper.
        var enter = context.GetType().GetMethod("Enter", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Direct engine context has no Enter method.");
        return (IDisposable?)enter.Invoke(context, null);
    }

    public static object UnwrapDirectEngine(object engine)
    {
        if (engine.GetType().FullName != "LiteDB.Client.Direct.DirectEngineLease")
        {
            return engine;
        }

        var property = engine.GetType().GetProperty("Engine", BindingFlags.Instance | BindingFlags.NonPublic)
                       ?? throw new InvalidOperationException("Direct engine lease has no Engine property.");
        return property.GetValue(engine)
               ?? throw new InvalidOperationException("Direct engine lease has no live engine.");
    }
}
