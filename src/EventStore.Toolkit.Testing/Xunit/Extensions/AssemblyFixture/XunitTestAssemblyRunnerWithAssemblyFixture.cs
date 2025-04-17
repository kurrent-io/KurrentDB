using Xunit.Sdk;

namespace EventStore.Toolkit.Testing.Xunit.Extensions.AssemblyFixture;

public class XunitTestAssemblyRunnerWithAssemblyFixture(
    ITestAssembly testAssembly,
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink,
    IMessageSink executionMessageSink,
    ITestFrameworkExecutionOptions executionOptions
)
    : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions) {
    readonly Dictionary<Type, object> _assemblyFixtureMappings = new Dictionary<Type, object>();

    protected override async Task AfterTestAssemblyStartingAsync() {
        await base.AfterTestAssemblyStartingAsync();

        await Aggregator.RunAsync(
            async
                () => {
                var fixturesAttrs = ((IReflectionAssemblyInfo)TestAssembly.Assembly).Assembly
                    .GetCustomAttributes(typeof(AssemblyFixtureAttribute), false)
                    .Cast<AssemblyFixtureAttribute>()
                    .ToList();

                foreach (var fixtureAttr in fixturesAttrs) {
                    var fixture = Activator.CreateInstance(fixtureAttr.FixtureType)!;

                    if (fixture is IAsyncLifetime asyncLifetime)
                        await asyncLifetime.InitializeAsync();

                    _assemblyFixtureMappings[fixtureAttr.FixtureType] = fixture;
                }
            }
        );
    }

    protected override async Task BeforeTestAssemblyFinishedAsync() {
        foreach (var disposable in _assemblyFixtureMappings.Values.OfType<IAsyncLifetime>())
            await Aggregator.RunAsync(disposable.DisposeAsync);

        foreach (var disposable in _assemblyFixtureMappings.Values.OfType<IDisposable>())
            Aggregator.Run(disposable.Dispose);

        await base.BeforeTestAssemblyFinishedAsync();
    }

    protected override Task<RunSummary> RunTestCollectionAsync(
        IMessageBus messageBus,
        ITestCollection testCollection,
        IEnumerable<IXunitTestCase> testCases,
        CancellationTokenSource cancellationTokenSource
    )
        => new XunitTestCollectionRunnerWithAssemblyFixture(
            _assemblyFixtureMappings,
            testCollection,
            testCases,
            DiagnosticMessageSink,
            messageBus,
            TestCaseOrderer,
            new ExceptionAggregator(Aggregator),
            cancellationTokenSource
        ).RunAsync();
}