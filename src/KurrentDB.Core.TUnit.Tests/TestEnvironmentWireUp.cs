// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Testing;
using TUnit.Core.Executors;

[assembly: System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]

namespace KurrentDB.Core.TUnit.Tests;

public class TestEnvironmentWireUp {
	[Before(Assembly)]
	public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Initialize();

	[After(Assembly)]
	public static ValueTask AfterAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Reset();
}
