// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;

namespace EventStore.Core.Tests;

public static class AssertEx {
	public static T IsType<T>(object o) {
		Assert.IsInstanceOf<T>(o);
		return (T)o;
	}

	public static async Task<TException> ThrowsAsync<TException>(Func<Task> code)
		where TException : Exception {
		var expected = default(TException);
		try {
			await code();
			Assert.Fail($"Expected exception of type: {typeof(TException)} but no exception was thrown");
		} catch (TException ex) {
			expected = ex;
		} catch (Exception ex) {
			Assert.Fail($"Expected exception of type: {typeof(TException)} but was {ex.GetType()} instead");
		}

		return expected;
	}

	public static async Task DoesNotThrowAsync<TException>(Func<Task> code, string message)
		where TException : Exception {
		try {
			await code();
		} catch (TException) {
			Assert.Fail(message);
		}
	}

	/// <summary>
	/// Asserts the given function will return true before the timeout expires.
	/// Repeatedly evaluates the function until true is returned or the timeout expires.
	/// Will return immediately when the condition is true.
	/// Evaluates the timeout until expired.
	/// Will not yield the thread by default, if yielding is required to resolve deadlocks set yieldThread to true.
	/// </summary>
	/// <param name="func">The function to evaluate.</param>
	/// <param name="timeout">A timeout in milliseconds. If not specified, defaults to 1000.</param>
	/// <param name="msg">A message to display if the condition is not satisfied.</param>
	/// <param name="onFail">Action to invoke on failure.</param>
	public static void IsOrBecomesTrue(Func<bool> func, TimeSpan? timeout = null,
		string msg = "AssertEx.IsOrBecomesTrue() timed out", Action onFail = null,
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) {

		if (SpinWait.SpinUntil(func, timeout ?? TimeSpan.FromMilliseconds(1000)))
			return;

		onFail?.Invoke();

		Assert.Fail($"{msg} in {memberName} {sourceFilePath}:{sourceLineNumber}");
	}
}
