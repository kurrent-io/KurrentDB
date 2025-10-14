// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Humanizer;
using Serilog;
using TUnit.Core.Executors;

[assembly: ToolkitTestConfigurator]
[assembly: TestExecutor<ToolkitTestExecutor>]

namespace KurrentDB.Api.Tests;

public class TestEnvironmentWireUp {
    [Before(Assembly)]
    public static ValueTask BeforeAssembly(AssemblyHookContext context) =>
        ToolkitTestEnvironment.Initialize(context.Assembly);

    [After(Assembly)]
    public static ValueTask AfterAssembly(AssemblyHookContext context) =>
        ToolkitTestEnvironment.Reset(context.Assembly);

    [BeforeEvery(Test)]
    public static void BeforeEveryTest(TestContext context) {
        // using static Log since the context has not been pushed yet in the Executor
        Log.ForContext("TestUid", context.Id).Verbose(
            "{TestClass} {TestName} {Status}",
            context.TestDetails.ClassType.Name,
            context.TestDetails.TestName,
            TestState.NotStarted
        );
    }

    [AfterEvery(Test)]
    public static void AfterEveryTest(TestContext context) {
        var elapsed = context.Result?.Duration ?? context.TestStart - context.TestEnd ?? TimeSpan.Zero;

        // using static Log since the context was already disposed of in the Executor
        Log.ForContext("TestUid", context.Id).Verbose(
            "{TestClass} {TestName} {Status} in {Elapsed}",
            context.TestDetails.ClassType.Name,
            context.TestDetails.TestName,
            context.Result!.State,
            elapsed.Humanize(2)
        );
    }
}
