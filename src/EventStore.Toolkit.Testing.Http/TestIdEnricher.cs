using Serilog.Core;
using Serilog.Events;

namespace EventStore.Toolkit.Testing.Http;

public class TestIdEnricher : ILogEventEnricher {
    string? _testRunId;

    public void UpdateId(string testRunId) => _testRunId = testRunId;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("TestRunId", _testRunId));
}