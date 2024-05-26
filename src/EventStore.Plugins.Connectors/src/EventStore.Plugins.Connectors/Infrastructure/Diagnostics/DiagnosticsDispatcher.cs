using System.Diagnostics;

namespace EventStore.Connectors.Infrastructure.Telemetry;

// [PublicAPI]
// public class DiagnosticsDispatcher : IDisposable {
//     ConcurrentDictionary<string, DiagnosticListener> Publishers { get; } = new();
//     
//     public void Dispatch<T>(string source, T diagnosticsData, bool checkEnabled = false) {
//         var diagnostics = Publishers
//             .GetOrAdd(source, static src => new(src));
//
//         if (checkEnabled && !diagnostics.IsEnabled(source))
//             return;
//             
//         diagnostics.Write(typeof(T).Name, diagnosticsData);
//     }
//     
//     public void Dispatch<TSource, T>(T diagnosticsData, bool checkEnabled = false) => 
//         Dispatch(typeof(TSource).Name, diagnosticsData);
//
//     public void Dispose() {
//         foreach (var publisher in Publishers.Values) 
//             publisher.Dispose();
//     }
// }

[PublicAPI]
public class DiagnosticsDispatcher(string source) {
    DiagnosticListener Dispatcher { get; } = new(source);
    
    public void Dispatch<T>(T diagnosticsData, bool checkEnabled = false) where T : class {
        if (checkEnabled && !Dispatcher.IsEnabled(source))
            return;
            
        Dispatcher.Write(typeof(T).Name, diagnosticsData);
    }
    
    public void Dispatch(string name, object diagnosticsData, bool checkEnabled = false) {
        if (checkEnabled && !Dispatcher.IsEnabled(source))
            return;
            
        Dispatcher.Write(name, diagnosticsData);
    }
}