// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace EventStore.Core.XUnit.Tests;

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
}
