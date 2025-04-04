// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

// ReSharper disable once CheckNamespace
namespace System.Threading.Tasks;

internal static class TaskExtensions {
	public static Task<T> WithTimeout<T>(this Task<T> task, TimeSpan? timeout = null) =>
		task.WithTimeout(timeout.HasValue ? (int)timeout.Value.TotalMilliseconds : 200);

	public static async Task<T> WithTimeout<T>(this Task<T> task, int timeout) {
		if (Debugger.IsAttached) {
			timeout = Timeout.Infinite;
		}
		using var cts = new CancellationTokenSource();
		if (task != await Task.WhenAny(task, Task.Delay(timeout, cts.Token)))
			throw new TimeoutException();

		await cts.CancelAsync();

		return task.Result;
	}

}
