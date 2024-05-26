// using System.Diagnostics;
//
// namespace EventStore.Streaming; 
//
// [PublicAPI]
// static class EnumerableExtensions {
//     [DebuggerStepThrough]
//     public static void ForEach<T>(this IEnumerable<T> source, Action<T> action) {
//         foreach (var item in source.EmptyIfNull())
//             action(item);
//     }
//
//     [DebuggerStepThrough]
//     public static void ForEach<T, TR>(this IEnumerable<T> source, Func<T, TR> function) {
//         foreach (var item in source.EmptyIfNull())
//             function(item);
//     }
//
//     [DebuggerStepThrough]
//     public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T>? source) => source ?? Enumerable.Empty<T>();
//
//     [DebuggerStepThrough]
//     public static IEnumerable<T> SafeUnion<T>(this IEnumerable<T>? first, IEnumerable<T>? second) => 
//         (first ?? new List<T>()).Union(second ?? Enumerable.Empty<T>());
//
//     [DebuggerStepThrough]
//     public static IEnumerable<T> SafeUnion<T>(this IEnumerable<T>? first, IEnumerable<T>? second, IEqualityComparer<T> comparer) =>
//         (first ?? Enumerable.Empty<T>()).Union(second ?? Enumerable.Empty<T>(), comparer);
//
//     [DebuggerStepThrough]
//     public static bool None<TSource>(this IEnumerable<TSource>? source) => !source?.Any() ?? false;
//
//     [DebuggerStepThrough]
//     public static bool None<TSource>(this IEnumerable<TSource>? source, Func<TSource, bool> predicate) => !source?.Any(predicate) ?? false;
// }