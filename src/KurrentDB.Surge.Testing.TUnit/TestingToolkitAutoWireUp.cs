// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Surge.Testing.TUnit.Logging;
using KurrentDB.Surge.Testing.TUnit;
using KurrentDB.Surge.Testing.TUnit.OpenTelemetry;
using Serilog;

namespace KurrentDB.Surge.Testing;

[PublicAPI]
public class TestingToolkitAutoWireUp {
    public static Faker Faker { get; } = new Faker();

    // Behaviour broke after moving to TUnit v0.55*
    // This should execute before [Before] but it does not. Not anymore.
    // [BeforeEvery(Assembly)]
    // public static void AssemblySetUp(AssemblyHookContext context) {
    //     new OtelServiceMetadata("TestingToolkit") {
    //         ServiceVersion   = "1.0.0",
    //         ServiceNamespace = "Kurrent.Client.Testing",
    //     }.UpdateEnvironmentVariables();
    //
    //     ApplicationContext.Initialize();
    //     Logging.Logging.Initialize(ApplicationContext.Configuration);
    // }

    // [AfterEvery(Assembly)]
    // public static async Task AssemblyCleanUp(AssemblyHookContext context) {
    //     await Logging.Logging.CloseAndFlushAsync().ConfigureAwait(false);
    // }

    static int _inititalized;

    public static void AssemblySetUp() {
        if (Interlocked.CompareExchange(ref _inititalized, 1, 0) != 0)
            return; // Already initialized

        new OtelServiceMetadata("TestingToolkit") {
            ServiceVersion   = "1.0.0",
            ServiceNamespace = "KurrentDB.Testing",
        }.UpdateEnvironmentVariables();

        ApplicationContext.Initialize();
        Logging.Initialize(ApplicationContext.Configuration);
    }

    public static async Task AssemblyCleanUp() {
        await Logging.CloseAndFlushAsync().ConfigureAwait(false);
    }

    // [BeforeEvery(Test)] [AfterEvery(Test)]
    // Unfortunatly the attribute triggers/runs AFTER IAsyncInitializer.InitializeAsync(),
    // therefor we must manually call the method from the TestFixture to capture all logs.
    //

    public static Task TestSetUp(TestContext context, CancellationToken ct = default) {
	    var testUid = Guid.NewGuid();

	    var loggerFactory = Logging
		    .CaptureTestLogs(testUid, _ => TestContext.Current.TestUid(defaultValue: Guid.Empty).Equals(testUid));

	    context.SetTestUid(testUid);
	    context.SetLoggerFactory(loggerFactory);
        context.SetOtelServiceMetadata(
            new(context.TestDetails.ClassType.Name) {
                ServiceInstanceId = testUid.ToString(),
                ServiceNamespace  = context.TestDetails.ClassType.Namespace
            }
        );

        Log.Verbose("#### Test {TestName} started", GetTestMethodName(context.TestDetails.TestId));

        return Task.CompletedTask;
    }

    public static Task TestCleanUp(TestContext context, CancellationToken ct = default) {
	    if (context.TryGetLoggerFactory(out var loggerFactory))
		    loggerFactory.Dispose();

        Log.Verbose(
            "#### Test {TestName} finished in {Elapsed}",
            GetTestMethodName(context.TestDetails.TestId),
            ((context.TestEnd ?? TimeProvider.System.GetUtcNow()) - context.TestStart.GetValueOrDefault()).Humanize(precision: 2)
        );

	    return Task.CompletedTask;
    }

    static string GetTestMethodName(string fullyQualifiedTestName) {
	    // Get the last segment after splitting by '.'
	    var methodNameWithPossibleParams = fullyQualifiedTestName.Split('.').Last();

	    // Remove any parameters or additional info after ':'
	    var colonIndex = methodNameWithPossibleParams.IndexOf(':');
	    return colonIndex >= 0
		    ? methodNameWithPossibleParams[..colonIndex]
		    : methodNameWithPossibleParams;
    }
}

public static class TestContextExtensions {
    const string TestUidKey = "$ToolkitTestUid";

    public static Guid SetTestUid(this TestContext context, Guid testUid) {
	    if (testUid == Guid.Empty)
		    throw new ArgumentException("Value cannot be empty.", nameof(testUid));

        context.ObjectBag[TestUidKey] = testUid;

        return testUid;
    }

    static bool TryGetTestUid(this TestContext? context, out Guid testUid) {
        if (context is not null
         && context.ObjectBag.TryGetValue(TestUidKey, out var value)
         && value is Guid uid) {
            testUid = uid;
            return true;
        }

        testUid = Guid.Empty;
        return false;
    }

    public static Guid TestUid(this TestContext? context, Guid? defaultValue = null) =>
	    !context.TryGetTestUid(out var testUid)
		    ? defaultValue ?? throw new InvalidOperationException("Testing toolkit test uid not found!")
		    : testUid;
}
