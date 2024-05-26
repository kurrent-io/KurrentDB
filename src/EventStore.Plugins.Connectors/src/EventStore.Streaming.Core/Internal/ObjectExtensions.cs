using System.Diagnostics;

namespace EventStore.Streaming;

internal static class ObjectExtensions {
    [DebuggerStepThrough]
    public static T As<T>(this object source) => (T)source;
}