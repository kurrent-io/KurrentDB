using Bogus;
using EventStore.Client;
using Serilog;
using Serilog.Extensions.Logging;

namespace EventStore.Testing.Fixtures;

[PublicAPI]
public partial class EventStoreFixture : IAsyncLifetime {
    static EventStoreFixture() => Logging.Initialize();

    protected EventStoreFixture(bool useIsolatedTestNode = false, ConfigureTestContainer? configureOptions = null) {
        Logger        = Log.ForContext<EventStoreFixture>();
        LoggerFactory = new SerilogLoggerFactory(Logger);
        Faker         = new Faker();
        TestRuns      = [];

        Service = useIsolatedTestNode
            ? new EventStoreTestNode(configureOptions)
            : SharedEventStoreTestNode.Instance;
    }

    List<Guid> TestRuns { get; }

    public EventStoreClientSettings EventStoreClientSettings => Service.Options.ClientSettings;

    public ILogger              Logger        { get; }
    public SerilogLoggerFactory LoggerFactory { get; }
    public EventStoreTestNode   Service       { get; }
    public Faker                Faker         { get; }
    public EventStoreClient     Streams       { get; private set; } = null!;

    public Func<Task> OnSetup    { get; init; } = () => Task.CompletedTask;
    public Func<Task> OnTearDown { get; init; } = () => Task.CompletedTask;

    public void CaptureTestRun(ITestOutputHelper outputHelper) {
        var testRunId = Logging.CaptureLogs(outputHelper);
        TestRuns.Add(testRunId);
        Logger.Information(">>> test run {TestRunId} {Operation} <<<", testRunId, "starting");
        Service.ReportStatus();
    }

    public async Task InitializeAsync() {
        await Service.Start();

        Streams = new EventStoreClient(EventStoreClientSettings);

        await OnSetup();
    }

    public async Task DisposeAsync() {
        try {
            await OnTearDown();
        }
        catch {
            // ignored
        }

        await Service.DisposeAsync();

        foreach (var testRunId in TestRuns) {
            Logger.Information(">>> test run {TestRunId} {Operation} <<<", testRunId, "completed");
            Logging.ReleaseLogs(testRunId);
        }
    }
}

public abstract class IntegrationTests<TFixture> : IClassFixture<TFixture> where TFixture : EventStoreFixture {
    protected IntegrationTests(ITestOutputHelper output, TFixture fixture) => Fixture = fixture.With(x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }
}