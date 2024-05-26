using System.Diagnostics;

namespace EventStore.Streaming;

static class EnumerableTaskExtensions {
    [DebuggerStepThrough]
    public static Task WhenAll(this IEnumerable<Task> source) => Task.WhenAll(source);

    [DebuggerStepThrough]
    public static Task<T[]> WhenAll<T>(this IEnumerable<Task<T>> source) => Task.WhenAll(source);
}