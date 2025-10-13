using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LiteDB.Utils.Extensions;

public static class CollectionExtensions
{
#if NETSTANDARD
    // ReSharper disable once InconsistentNaming
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ICollection<T> PreventChangeFullFX<T>(this ICollection<T> collection)
    {
        return collection.ToList();
    }
#else
    // ReSharper disable once InconsistentNaming
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static ICollection<T> PreventChangeFullFX<T>(this ICollection<T> collection)
    {
        return collection;
    }
#endif
}