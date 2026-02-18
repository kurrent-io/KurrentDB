// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Time;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests;

public static class AssertEx {
	public static void IsOrBecomesTrue(
		Func<bool> func,
		TimeSpan? timeout = null,
		string msg = "AssertEx.IsOrBecomesTrue() timed out",
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) {

		Assert.True(
			SpinWait.SpinUntil(func, timeout ?? TimeSpan.FromMilliseconds(1000)),
			$"{msg} in {memberName} {sourceFilePath}:{sourceLineNumber}");
	}

	public static async Task IsOrBecomesTrueAsync(
		Func<Task<bool>> func,
		TimeSpan? timeout = null,
		string msg = "AssertEx.IsOrBecomesTrueAsync() timed out",
		[CallerMemberName] string memberName = "",
		[CallerFilePath] string sourceFilePath = "",
		[CallerLineNumber] int sourceLineNumber = 0) {

		timeout ??= TimeSpan.FromMilliseconds(1000);
		var start = Instant.Now;

		while (!await func()) {
			if (Instant.Now.ElapsedTimeSince(start) >= timeout)
				Assert.Fail($"{msg} in {memberName} {sourceFilePath}:{sourceLineNumber}");
			await Task.Delay(10);
		}
	}
}
