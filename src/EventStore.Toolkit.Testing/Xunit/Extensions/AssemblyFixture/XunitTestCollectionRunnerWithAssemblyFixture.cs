using Xunit.Sdk;

namespace EventStore.Toolkit.Testing.Xunit.Extensions.AssemblyFixture;

public class XunitTestCollectionRunnerWithAssemblyFixture(
    Dictionary<Type, object> assemblyFixtureMappings,
    ITestCollection testCollection,
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink,
    IMessageBus messageBus,
    ITestCaseOrderer testCaseOrderer,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource
)
    : XunitTestCollectionRunner(
        testCollection,
        testCases,
        diagnosticMessageSink,
        messageBus,
        testCaseOrderer,
        aggregator,
        cancellationTokenSource
    ) {
    readonly IMessageSink _diagnosticMessageSink = diagnosticMessageSink;

    protected override Task<RunSummary> RunTestClassAsync(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases) {
        // Don't want to use .Concat + .ToDictionary because of the possibility of overriding types,
        // so instead we'll just let collection fixtures override assembly fixtures.
        var combinedFixtures = new Dictionary<Type, object>(assemblyFixtureMappings);
        foreach (var kvp in CollectionFixtureMappings)
            combinedFixtures[kvp.Key] = kvp.Value;

        // We've done everything we need, so let the built-in types do the rest of the heavy lifting
        return new XunitTestClassRunner(
            testClass,
            @class,
            testCases,
            _diagnosticMessageSink,
            MessageBus,
            TestCaseOrderer,
            new ExceptionAggregator(Aggregator),
            CancellationTokenSource,
            combinedFixtures
        ).RunAsync();
    }
}