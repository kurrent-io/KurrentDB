namespace EventStore.Streaming;

internal static class ActionExtensions {
	public static Task InvokeAsync(this Action action) {
		action();
		return Task.CompletedTask;
	}

	public static Task InvokeAsync<T1>(this Action<T1> action, T1 arg1) {
		action(arg1);
		return Task.CompletedTask;
	}
	
	public static Task InvokeAsync<T1, T2>(this Action<T1, T2> action, T1 arg1, T2 arg2) {
		action(arg1, arg2);
		return Task.CompletedTask;
	}
	
	public static Task InvokeAsync<T1, T2, T3>(this Action<T1, T2, T3> action, T1 arg1, T2 arg2, T3 arg3) {
		action(arg1, arg2, arg3);
		return Task.CompletedTask;
	}
}