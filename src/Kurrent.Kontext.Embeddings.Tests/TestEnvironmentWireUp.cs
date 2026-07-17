// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using TUnit.Core.Executors;
using TUnit.Core.Interfaces;
using Kurrent.Kontext.Embeddings.Tests;

[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]
[assembly: Timeout(60_000)]
[assembly: ParallelLimiter<SingleThreaded>] // ONNX sessions are heavy; run the suite serially.

namespace Kurrent.Kontext.Embeddings.Tests;

public sealed class SingleThreaded : IParallelLimit {
	public int Limit => 1;
}

public class TestEnvironmentWireUp {
	[Before(Assembly)]
	public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Initialize();

	[After(Assembly)]
	public static ValueTask AfterAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Reset();
}
