// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Ammeter;
using KurrentDB.Testing;
using TUnit.Core.Executors;
using TUnit.Core.Interfaces;

[assembly: System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
[assembly: ParallelLimiter<EnvironmentParallelLimit>]
[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]

namespace KurrentDB.Ammeter;

public class TestEnvironmentWireUp {
	[Before(Assembly)]
	public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Initialize(context.Assembly);

	[After(Assembly)]
	public static ValueTask AfterAssembly(AssemblyHookContext context) =>
		ToolkitTestEnvironment.Reset(context.Assembly);
}

public record EnvironmentParallelLimit : IParallelLimit {
	public int Limit {
		get {
			var limit = TestContext.Configuration.Get("ParallelLimit")?.Apply(int.Parse);
			return limit is null or -1
				? Environment.ProcessorCount
				: limit.Value;
		}
	}
}
