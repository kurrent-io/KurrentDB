using System.Diagnostics;

namespace EventStore.Toolkit.Testing;

public static class WithExtension {
	[DebuggerStepThrough]
	public static T With<T>(this T instance, Action<T> action, bool when = true) {
		if (when)
			action(instance);

		return instance;
	}

	[DebuggerStepThrough]
	public static T With<T>(this T instance, Func<T, T> action, bool when = true) => when ? action(instance) : instance;

	[DebuggerStepThrough]
	public static T With<T>(this T instance, Action<T> action, Func<T, bool> when) {
		if (when is null)
			throw new ArgumentNullException(nameof(when));

		if (when(instance))
			action(instance);

		return instance;
	}

	[DebuggerStepThrough]
	public static TR WithResult<T, TR>(this T instance, Func<T, TR> action) {
		if (action is null)
			throw new ArgumentNullException(nameof(action));

		return action(instance);
	}

	[DebuggerStepThrough]
	public static T With<T>(this T instance, Func<T, T> action, Func<T, bool> when) {
		if (when is null)
			throw new ArgumentNullException(nameof(when));

		return when(instance) ? action(instance) : instance;
	}

	[DebuggerStepThrough]
	public static T With<T>(this T instance, Action<T> action, Func<bool> when) {
		if (when is null)
			throw new ArgumentNullException(nameof(when));

		if (when())
			action(instance);

		return instance;
	}

	[DebuggerStepThrough]
	public static T With<T>(this T instance, Func<T, T> action, Func<bool> when) {
		if (when is null)
			throw new ArgumentNullException(nameof(when));

		return when() ? action(instance) : instance;
	}
}