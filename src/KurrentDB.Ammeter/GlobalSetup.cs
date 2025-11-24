// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Ammeter;
using KurrentDB.Testing;
using TUnit.Core.Executors;

[assembly: System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
[assembly: ParallelLimiter<EnvironmentParallelLimit>]
[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]

namespace KurrentDB.Ammeter;

public class TestEnvironmentWireUp {
	[Before(TestDiscovery)]
	public static async ValueTask BeforeDiscovery() {
		// Initialize the configuration before discovery so that the node shim can find the configuration
		await ToolkitTestEnvironment.Initialize();
	}

	[After(TestSession)]
	public static async ValueTask AfterSession() {
		await ToolkitTestEnvironment.Reset();
	}
}
