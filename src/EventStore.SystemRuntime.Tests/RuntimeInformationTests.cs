// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime;
using Xunit;

namespace EventStore.SystemRuntime.Tests;

public class RuntimeInformationTests {
	[Fact]
	public void can_get_runtime_version() {
		Assert.NotNull(RuntimeInformation.RuntimeVersion);
	}

	[Fact]
	public void can_get_runtime_mode() {
		Assert.True(RuntimeInformation.RuntimeMode > 0);
	}
}
