using Bogus;
using Humanizer;
using KurrentDB.Testing.Logging;
using KurrentDB.Testing.OpenTelemetry;
using Serilog;

namespace KurrentDB.Testing;

[PublicAPI]
public static class TestingManager {
    public static Faker Faker { get; } = new();

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

    static int Inititalized;

    public static void AssemblySetUp() {
        if (Interlocked.CompareExchange(ref Inititalized, 1, 0) != 0)
            return; // Already initialized

        new OtelServiceMetadata("TestingToolkit") {
            ServiceVersion   = "1.0.0",
            ServiceNamespace = "Kurrent.Toolkit.Testing",
        }.UpdateEnvironmentVariables();

        ApplicationContext.Initialize();

        LoggingContext.Initialize(ApplicationContext.Configuration);
    }

    public static async Task AssemblyCleanUp() {
        await LoggingContext.CloseAndFlushAsync().ConfigureAwait(false);
    }

    // [BeforeEvery(Test)] [AfterEvery(Test)]
    // Unfortunatly the attribute triggers/runs AFTER IAsyncInitializer.InitializeAsync(),
    // therefor we must manually call the method from the TestFixture to capture all logs.
    //

    public static Task TestSetUp(TestContext context, CancellationToken ct = default) {
	    var testUid = context.AssignTestUid(Guid.NewGuid());

	    context.ConfigureLogging(testUid);

        context.ConfigureOtel(
            new(context.TestDetails.ClassType.Name) {
                ServiceInstanceId = testUid.ToString(),
                ServiceNamespace  = context.TestDetails.ClassType.Namespace
            }
        );

        Log.Verbose("#### Test {TestName} started", GetTestMethodName(context.TestDetails.TestId));

        return Task.CompletedTask;
    }

    public static async Task TestCleanUp(TestContext context, CancellationToken ct = default) {
	    await context.LoggerProvider().DisposeAsync();

	    Log.Verbose(
            "#### Test {TestName} finished in {Elapsed}",
            GetTestMethodName(context.TestDetails.TestId),
            ((context.TestEnd ?? TimeProvider.System.GetUtcNow()) - context.TestStart).Humanize(precision: 2));
    }

    public static ValueTask OnTestRetry(TestContext testContext, int retryAttempt) {
	    Log.Warning(
		    "#### Test {TestName} retrying (attempt {Attempt})",
		    GetTestMethodName(testContext.TestDetails.TestId), retryAttempt + 1);

	    return ValueTask.CompletedTask;
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
