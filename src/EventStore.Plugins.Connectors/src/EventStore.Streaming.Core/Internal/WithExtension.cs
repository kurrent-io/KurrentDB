using System.Diagnostics;

namespace EventStore.Streaming;

internal static class WithExtension {
    [DebuggerStepThrough]
    public static T With<T>(this T instance, Action<T> update, bool when = true) {
        if (when)
            update(instance);

        return instance;
    }

    [DebuggerStepThrough]
    public static T With<T>(this T instance, Func<T, T> update, bool when = true) => when ? update(instance) : instance;

    [DebuggerStepThrough]
    public static T With<T>(this T instance, Action<T> update, Func<T, bool> when) {
        Ensure.NotNull(when, nameof(when));

        if (when(instance))
            update(instance);

        return instance;
    }

    [DebuggerStepThrough]
    public static T With<T>(this T instance, Func<T, T> update, Func<T, bool> when) {
        Ensure.NotNull(when, nameof(when));
        return when(instance) ? update(instance) : instance;
    }

    [DebuggerStepThrough]
    public static T With<T>(this T instance, Action<T> update, Func<bool> when) {
        Ensure.NotNull(when, nameof(when));
        if (when())
            update(instance);

        return instance;
    }

    [DebuggerStepThrough]
    public static T With<T>(this T instance, Func<T, T> update, Func<bool> when) {
        Ensure.NotNull(when, nameof(when));
        return when() ? update(instance) : instance;
    }
}