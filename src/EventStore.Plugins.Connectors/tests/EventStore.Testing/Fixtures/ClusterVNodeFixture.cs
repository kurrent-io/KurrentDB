using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using EventStore.Core;
using EventStore.Core.Bus;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace EventStore.Testing.Fixtures;

[PublicAPI]
[SuppressMessage("Performance", "CA1822:Mark members as static")]
public class ClusterVNodeFixture : IAsyncLifetime {
    static ClusterVNodeFixture() {
        Logging.Initialize();
        ClusterVNodeActivator.Initialize(); // fake trigger
    }

    protected ClusterVNodeFixture() {
        LoggerFactory = new SerilogLoggerFactory(Log.Logger);
        Logger        = LoggerFactory.CreateLogger<ClusterVNodeFixture>();
        Faker         = new Faker();
        TestRuns      = [];
    }

    List<Guid> TestRuns { get; }

    public ILogger            Logger        { get; }
    public ILoggerFactory     LoggerFactory { get; }
    public Faker              Faker         { get; }

    public Func<Task> OnSetup    { get; init; } = () => Task.CompletedTask;
    public Func<Task> OnTearDown { get; init; } = () => Task.CompletedTask;

    public IPublisher          Publisher   => ClusterVNodeActivator.Publisher;
    public ISubscriber         Subscriber  => ClusterVNodeActivator.Subscriber;
    public ClusterVNodeOptions NodeOptions => ClusterVNodeActivator.NodeOptions;

    public void CaptureTestRun(ITestOutputHelper outputHelper) {
        var testRunId = Logging.CaptureLogs(outputHelper);
        TestRuns.Add(testRunId);
        Logger.LogInformation(">>> test run {TestRunId} {Operation} <<<", testRunId, "starting");
    }

    public async Task InitializeAsync() {
        await OnSetup();
    }

    public async Task DisposeAsync() {
        try {
            await OnTearDown();
        }
        catch {
            // ignored
        }

        foreach (var testRunId in TestRuns) {
            Logger.LogInformation(">>> test run {TestRunId} {Operation} <<<", testRunId, "completed");
            Logging.ReleaseLogs(testRunId);
        }
    }

    public ILogger<T> CreateLogger<T>() => LoggerFactory.CreateLogger<T>();

    public ILogger CreateLogger(string categoryName) => LoggerFactory.CreateLogger(categoryName);

    /// <summary>
    ///  Get a cancellation token source for a test.
    ///  If the test is running in a debugger, the cancellation token will be infinite.
    ///  Otherwise, the cancellation token timeout will be set to 10 seconds.
    /// </summary>
    /// <param name="timeout">The timeout for the cancellation token.</param>
    public CancellationTokenSource GetTestCancellationSource(TimeSpan? timeout = null) =>
        new(Debugger.IsAttached ? DotNext.Threading.Timeout.Infinite : TimeSpan.FromSeconds(10));

    public async Task TestWithTimeout(TimeSpan timeout, Func<CancellationTokenSource, Task> test) {
        try {
            using var cts = GetTestCancellationSource(timeout);
            await test(cts);
        }
        catch (OperationCanceledException) {
            // TODO SS: need to know if someone cancelled the cts, or the timeout triggered to fix this
            Assert.Fail($"Test execution timed out after {timeout.Humanize()}");
        }
    }

    public async Task TestWithTimeoutInSeconds(int timeoutSeconds, Func<CancellationTokenSource, Task> test) =>
        await TestWithTimeout(TimeSpan.FromSeconds(timeoutSeconds), test);

    public Task TestWithTimeout(Func<CancellationTokenSource, Task> test) =>
        TestWithTimeout(TimeSpan.FromSeconds(10), test);
}

public abstract class ClusterVNodeTests<TFixture> where TFixture : ClusterVNodeFixture {
    protected ClusterVNodeTests(ITestOutputHelper output, TFixture fixture) => Fixture = fixture.With(x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }
}