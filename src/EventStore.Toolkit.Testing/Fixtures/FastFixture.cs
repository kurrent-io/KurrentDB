using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Extensions.Logging;

namespace EventStore.Toolkit.Testing.Fixtures;

[PublicAPI]
public partial class FastFixture : IAsyncLifetime, IAsyncDisposable {
    static readonly ILogger FixtureLogger;

    static FastFixture() {
        // Logging.Initialize();
        FixtureLogger = Log.ForContext<FastFixture>();

        AssertionOptions.AssertEquivalencyUsing(
            options => options
                .Using<ReadOnlyMemory<byte>>(
                    ctx => ctx.Subject.Span.SequenceEqual(ctx.Expectation.Span).Should()
                        .BeTrue(ctx.Because, ctx.BecauseArgs)
                )
                .WhenTypeIs<ReadOnlyMemory<byte>>()
                .Using<Memory<byte>>(
                    ctx => ctx.Subject.Span.SequenceEqual(ctx.Expectation.Span).Should()
                        .BeTrue(ctx.Because, ctx.BecauseArgs)
                )
                .WhenTypeIs<Memory<byte>>()
        );
    }

    List<Guid> TestRuns { get; } = [];

    protected ITestOutputHelper OutputHelper { get; private set; } = null!;
    public    ILogger           Logger       => FixtureLogger;

    public SerilogLoggerFactory LoggerFactory { get; } = new(FixtureLogger);
    public Faker                Faker         { get; } = new();
    public FakeTimeProvider     TimeProvider  { get; } = new();

    public Func<Task> OnSetup    { get; init; } = () => Task.CompletedTask;
    public Func<Task> OnTearDown { get; init; } = () => Task.CompletedTask;

    public void CaptureTestRun(ITestOutputHelper outputHelper) {
        OutputHelper = outputHelper;

        var testRunId = Logging.CaptureLogs(outputHelper);
        TestRuns.Add(testRunId);
        FixtureLogger.Information(">>> test run {TestRunId} {Operation} <<<", testRunId, "started");
    }

    public async Task InitializeAsync() {
        await OnSetup();
    }

    public async Task DisposeAsync() {
        await OnTearDown();

        // try {
        // 	await OnTearDown();
        // }
        // catch(Exception ex) {
        // 	FixtureLogger.Warning(ex, "fixture tear down error");
        // }

        foreach (var testRunId in TestRuns) {
            FixtureLogger.Information(">>> test run {TestRunId} {Operation} <<<", testRunId, "completed");
            Logging.ReleaseLogs(testRunId);
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsync();
}

public abstract class FastTests<TFixture> : IClassFixture<TFixture> where TFixture : FastFixture {
    protected FastTests(ITestOutputHelper output, TFixture fixture) => Fixture = fixture.With(x => x.CaptureTestRun(output));

    protected TFixture Fixture { get; }
}

public abstract class FastTests(ITestOutputHelper output, FastFixture fixture) : FastTests<FastFixture>(output, fixture);