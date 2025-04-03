// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DotNext;
using static System.Threading.Timeout;

namespace EventStore.Core.Tests;

public static class TaskExtensions {
	public static async Task WithTimeout(this Task task, TimeSpan timeout, Action onFail = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) {
		Debug.Assert(task is not null);

		if (Debugger.IsAttached) {
			timeout = InfiniteTimeSpan;
		}

		await task
			.WaitAsync(timeout)
			.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

		if (!task.IsCompleted) {
			onFail?.Invoke();
			throw new TimeoutException($"Timed out waiting for task at: {memberName} {sourceFilePath}:{sourceLineNumber}");
		}

		await task;
	}

	public static Task WithTimeout(this Task task, int timeoutMs = 10000, Action onFail = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
		=> WithTimeout(task, TimeSpan.FromMilliseconds(timeoutMs), onFail, memberName, sourceFilePath,
			sourceLineNumber);

	public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout, Action onFail = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) {

		if (Debugger.IsAttached) {
			timeout = InfiniteTimeSpan;
		}

		await task.As<Task>()
			.WaitAsync(timeout)
			.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

		if (!task.IsCompleted) {
			onFail?.Invoke();
			throw new TimeoutException(
				$"Timed out waiting for task at: {memberName} {sourceFilePath}:{sourceLineNumber}");
		}

		return await task;
	}

	public static Task<T> WithTimeout<T>(this Task<T> task, int timeoutMs = 10000, Action onFail = null,
		[CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0)
		=> WithTimeout<T>(task, TimeSpan.FromMilliseconds(timeoutMs), onFail, memberName, sourceFilePath,
			sourceLineNumber);
}
