// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using TUnit.Core.Executors;
using Kurrent.Kontext.Tests;

[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]
[assembly: Timeout(60_000)]

namespace Kurrent.Kontext.Tests;

public class TestEnvironmentWireUp {
	[Before(Assembly)]
	public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Initialize();

	[After(Assembly)]
	public static ValueTask AfterAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Reset();
}
