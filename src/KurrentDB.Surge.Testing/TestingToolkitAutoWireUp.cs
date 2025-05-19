using Kurrent.Surge;
using KurrentDB.Surge.Testing.OpenTelemetry;
using Serilog;

namespace KurrentDB.Surge.Testing;

public class TestingToolkitAutoWireUp {
    public static Faker Faker { get; } = new Faker();

    [BeforeEvery(Assembly)]
    public static void AssemblySetUp(AssemblyHookContext context) {
        new OtelServiceMetadata("TestingToolkit") {
            ServiceVersion    = "1.0.0",
            ServiceNamespace  = "Kurrent.Surge.Testing",
        }.UpdateEnvironmentVariables();

        ApplicationContext.Initialize();
        Logging.Initialize(ApplicationContext.Configuration);
    }

    [AfterEvery(Assembly)]
    public static void AssemblyCleanUp(AssemblyHookContext context) {
        Logging.CloseAndFlush();
    }

    // [BeforeEvery(Test)] [AfterEvery(Test)]
    // Unfortunatly the attribute triggers/runs AFTER IAsyncInitializer.InitializeAsync(),
    // therefor we must manually call the method from the TestFixture to capture all logs.
    //

    public static Task TestSetUp(TestContext context, CancellationToken cancellationToken = default) {
        var testUid = Guid.NewGuid();

        var loggerFactory = Logging.CaptureTestLogs(
            testUid, () => TestContext.Current.TryGetTestUid(out var uid) ? uid : Guid.Empty
        );

        context.SetTestUid(testUid);
        context.SetLoggerFactory(loggerFactory);
        context.SetOtelServiceMetadata(
            new(context.TestDetails.TestClass.Name) {
                ServiceInstanceId = testUid.ToString(),
                ServiceNamespace  = context.TestDetails.TestClass.Namespace
            }
        );

        Log.Verbose("#### Test Started: {TestName}", context.TestDetails.TestId);

        return Task.CompletedTask;
    }

    public static async Task TestCleanUp(TestContext context, CancellationToken cancellationToken = default) {
        Log.Verbose(
            "#### Test Finished in {Elapsed} after {Attempt} attempt(s): {TestName}",
            (TimeProvider.System.GetUtcNow() - context.TestStart.GetValueOrDefault()).Humanize(precision: 2),
            context.TestDetails.CurrentRepeatAttempt + 1,
            context.TestDetails.TestId
        );

        if (context.TryGetLoggerFactory(out var loggerFactory))
            await loggerFactory.DisposeAsync();
    }
}

public static class TestContextExtensions {
    const string TestUidKey = "$ToolkitTestUid";

    public static void SetTestUid(this TestContext context, Guid testUid) {
        Ensure.NotEmpty(testUid);
        context.ObjectBag[TestUidKey] = testUid;
    }

    public static bool TryGetTestUid(this TestContext? context, out Guid testUid) {
        if (context is not null
         && context.ObjectBag.TryGetValue(TestUidKey, out var value)
         && value is Guid uid) {
            testUid = uid;
            return true;
        }

        testUid = Guid.Empty;
        return false;
    }

    public static Guid TestUid(this TestContext? context) =>
        !context.TryGetTestUid(out var testUid)
            ? throw new InvalidOperationException("Testing toolkit test uid not found!")
            : testUid;
}
