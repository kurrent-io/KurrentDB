// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Humanizer;
using KurrentDB.Testing;
using KurrentDB.Testing.Logging;
using KurrentDB.Testing.OpenTelemetry;
using Serilog;
using TUnit.Core.Executors;
using TUnit.Core.Interfaces;

[assembly: TestExecutor<ToolkitTestExecutor>]

namespace KurrentDB.Testing;

public class ToolkitTestExecutor : ITestExecutor, IHookExecutor {
	public ValueTask ExecuteAfterTestDiscoveryHook(MethodMetadata hookMethodInfo, TestDiscoveryContext context, Func<ValueTask> action) {
		// to simplify the logging and tracing logic, we configure all tests here
		// this way we ensure that all tests have a unique ID and are properly configured for OTEL
		Parallel.ForEach(context.AllTests, testContext => {
			var testUid = testContext.AssignTestUid();

            testContext.ConfigureOtel(new(testContext.TestDetails.ClassType.Name) {
				ServiceInstanceId = testUid,
				ServiceNamespace  = testContext.TestDetails.ClassType.Namespace
			});
		});

		return ValueTask.CompletedTask;
	}

	public ValueTask ExecuteBeforeAssemblyHook(MethodMetadata hookMethodInfo, AssemblyHookContext context, Func<ValueTask> action) {
		ToolkitTestingContext.Initialize();
		Log.Verbose("#### Executing {TestCount} tests in assembly {AssemblyName}", context.TestCount, context.Assembly.GetName().Name);
		return ValueTask.CompletedTask;
	}

	public ValueTask ExecuteAfterAssemblyHook(MethodMetadata hookMethodInfo, AssemblyHookContext context, Func<ValueTask> action) {
		Log.Verbose("#### Executed {TestCount} tests in assembly {AssemblyName}", context.TestCount, context.Assembly.GetName().Name);
		return Log.CloseAndFlushAsync();
	}

	public ValueTask ExecuteBeforeTestHook(MethodMetadata hookMethodInfo, TestContext context, Func<ValueTask> action) {
		Log.Verbose("#### Executing {TestName}", context.TestDetails.TestName);
		return ValueTask.CompletedTask;
	}

	public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action) {
		var testUid = context.GetTestUid();

        using var subscription = ToolkitTestingContext.AttachTestUidToLogEvents(testUid);

		await using var loggerProvider = ToolkitTestingContext.CreateLoggerProvider(testUid, context.TestDetails.ClassType.FullName);

		context.SetLoggerProvider(loggerProvider);;

        await action();

		// context.TestStart = TimeProvider.System.GetUtcNow();
		//
		// try {
		// 	await action();
		// }
		// finally {
		// 	context.TestEnd = TimeProvider.System.GetUtcNow();
		//
		// 	context.Timings.Add(new("Execution", context.TestStart, context.TestEnd.Value));
		//
		// 	var elapsed = context.TestEnd.Value - context.TestStart;
		//
		// 	Log.Verbose(
		// 		"#### Executed {TestName} {Status} in {Elapsed}",
		// 		context.TestDetails.TestName,
		// 		context.Result,
		// 		elapsed.Humanize(precision: 2));
		// }
	}

	public ValueTask ExecuteAfterTestHook(MethodMetadata hookMethodInfo, TestContext context, Func<ValueTask> action) {
		if (context.Result?.Duration is null) {
			// this should never happen, but if it does, we want to know about it
			Log.Fatal("#### Test {TestName} has no result or the result does not have a duration?! Wait... WHAT?!", context.TestDetails.TestName);
			return ValueTask.CompletedTask;
		}

		var elapsed = context.Result.Duration.GetValueOrDefault();

		Log.Verbose(
			"#### Test {TestName} {TestState} in {Elapsed}",
			context.TestDetails.TestName,
			context.Result.State,
			elapsed.Humanize(precision: 2));

		return ValueTask.CompletedTask;
	}

	#region . Unused Hooks .

    public ValueTask ExecuteBeforeTestDiscoveryHook(MethodMetadata hookMethodInfo, BeforeTestDiscoveryContext context, Func<ValueTask> action) =>
        ValueTask.CompletedTask;

    public ValueTask ExecuteBeforeTestSessionHook(MethodMetadata hookMethodInfo, TestSessionContext context, Func<ValueTask> action) =>
        ValueTask.CompletedTask;

    public ValueTask ExecuteAfterTestSessionHook(MethodMetadata hookMethodInfo, TestSessionContext context, Func<ValueTask> action) =>
        ValueTask.CompletedTask;

    public ValueTask ExecuteBeforeClassHook(MethodMetadata hookMethodInfo, ClassHookContext context, Func<ValueTask> action) =>
        ValueTask.CompletedTask;

    public ValueTask ExecuteAfterClassHook(MethodMetadata hookMethodInfo, ClassHookContext context, Func<ValueTask> action) =>
        ValueTask.CompletedTask;

	#endregion
}
