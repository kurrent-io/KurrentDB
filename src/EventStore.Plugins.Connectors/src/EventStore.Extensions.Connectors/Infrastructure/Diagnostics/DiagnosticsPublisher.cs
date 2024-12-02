using System.Diagnostics;
using EventStore.Toolkit;
using static EventStore.Connectors.Diagnostics.DiagnosticsCollectionMode;

namespace EventStore.Connectors.Diagnostics;

[PublicAPI]
public sealed class DiagnosticsPublisher : IDisposable {
    public DiagnosticsPublisher(string diagnosticsSourceName) {
        Ensure.NotNullOrWhiteSpace(diagnosticsSourceName);
        Publisher = new(diagnosticsSourceName);
    }

    DiagnosticListener Publisher { get; }

    public string SourceName => Publisher.Name;

    public void Publish<T>(T diagnosticEvent, bool checkEnabled = false) where T : class {
        var typeName = diagnosticEvent.GetType().Name; // because T might not be the actual type

        if (checkEnabled && !Publisher.IsEnabled(typeName))
            return;

        Publisher.Write(typeName, diagnosticEvent);
    }

    public void Publish(string eventName, object eventPayload, bool checkEnabled = false) {
        var typeName = eventPayload.GetType().Name;

        if (checkEnabled && !Publisher.IsEnabled(typeName))
            return;

        Publisher.Write(eventName, eventPayload);
    }

    public DiagnosticsData PublishDiagnosticsData(Dictionary<string, object?> data, DiagnosticsCollectionMode mode = Partial) {
        var payload = new DiagnosticsData {
            Source         = Publisher.Name,
            Data           = data,
            CollectionMode = mode
        };

        Publish(nameof(DiagnosticsData), payload);

        return payload;
    }

    public DiagnosticsData PublishDiagnosticsData(string eventName, Dictionary<string, object?> data, DiagnosticsCollectionMode mode = Event) {
        if (eventName == nameof(DiagnosticsData))
            throw new ArgumentException("Event name cannot be PluginDiagnosticsData", nameof(eventName));

        var payload = new DiagnosticsData {
            Source         = Publisher.Name,
            EventName      = eventName,
            Data           = data,
            CollectionMode = mode
        };

        Publish(eventName, payload);

        return payload;
    }

    public void Dispose() => Publisher.Dispose();

    public static DiagnosticsPublisher Create(string source) => new(source);

    public static DiagnosticsPublisher Create<T>() => new(typeof(T).Name);
}