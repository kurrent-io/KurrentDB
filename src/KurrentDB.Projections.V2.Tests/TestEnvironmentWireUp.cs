// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using Humanizer;
using KurrentDB.Projections.Core.Messages;
using Serilog;
using TUnit.Core.Executors;

[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]
[assembly: Timeout(30_000)]

namespace KurrentDB.Projections.V2.Tests;

public class TestEnvironmentWireUp {
	[Before(Assembly)]
	public static ValueTask BeforeAssembly(AssemblyHookContext context) {
		// Force projection assemblies to load before InMemoryBus static constructor
		// scans AppDomain assemblies. Without this, projection message types won't be
		// registered in the bus, causing "Unexpected message type" errors.
		RuntimeHelpers.RunClassConstructor(typeof(ProjectionCoreServiceMessage).TypeHandle);
		RuntimeHelpers.RunClassConstructor(typeof(CoreProjectionManagementMessage).TypeHandle);
		return ToolkitTestEnvironment.Initialize();
	}

	[After(Assembly)]
	public static ValueTask AfterAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Reset();

	[BeforeEvery(Test)]
	public static void BeforeEveryTest(TestContext context) {
		Log.ForContext("TestUid", context.Id).Verbose(
			"{TestClass} {TestName} {Status}",
			context.Metadata.TestDetails.ClassType.Name,
			context.Metadata.TestDetails.TestName,
			TestState.NotStarted
		);
	}

	[AfterEvery(Test)]
	public static void AfterEveryTest(TestContext context) {
		var elapsed = context.Execution.Result?.Duration ?? context.Execution.TestEnd - context.Execution.TestStart ?? TimeSpan.Zero;

		Log.ForContext("TestUid", context.Id).Verbose(
			"{TestClass} {TestName} {Status} in {Elapsed}",
			context.Metadata.TestDetails.ClassType.Name,
			context.Metadata.TestDetails.TestName,
			context.Execution.Result!.State,
			elapsed.Humanize(2)
		);
	}
}
