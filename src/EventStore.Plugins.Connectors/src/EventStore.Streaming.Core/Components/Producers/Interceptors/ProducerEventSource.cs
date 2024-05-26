// // ReSharper disable MemberCanBePrivate.Global
//
// using System.Diagnostics;
// using System.Reactive;
// using System.Reactive.Linq;
// using EventStore.Streaming.Interceptors;
// using EventStore.Streaming.Producers.Diagnostics;
// using EventStore.Streaming.Routing;
// using EventStore.Streaming.Routing.Broadcasting;
//
// namespace EventStore.Streaming.Producers.Diagnostics;
//
// public interface IComponentDiagnosticSource {
//     void RaiseEvent<T>(T evt) where T : InterceptorEvent;
// }
//
// public class ComponentDiagnosticSource(string name) : DiagnosticListener(name), IComponentDiagnosticSource {
//     public void RaiseEvent<T>(T evt) where T : InterceptorEvent {
//         if (IsEnabled() && IsEnabled(typeof(T).Name))
//             Write(typeof(T).Name, evt);
//     }
// }
//
// public class ProducerDiagnosticSource() : ComponentDiagnosticSource("EventStore.Streaming.Producer");
//
// // public class Observer : IObserver<KeyValuePair<string, object?>>
// // {
// //     public void OnCompleted() => 
// //         Console.WriteLine("DiagnosticListener was disposed!");
// //  
// //     public void OnError(Exception error) { }
// //  
// //     public void OnNext(KeyValuePair<string, object?> value) => 
// //         Console.WriteLine($"{value.Key}: {(string)value.Value!}");
// // }
//
// public class InterceptorEventObserver : IObserver<KeyValuePair<string, object?>> {
//     protected InterceptorEventObserver(string? name = null) {
//         Name   = name ?? GetType().Name.Replace("Module", "");
//         Router = new(MessageBroadcasterStrategy.Sequential);
//     }
//
//     MessageRouter Router { get; }
//
//     public string Name { get; }
//
//     
//     void IObserver<KeyValuePair<string, object?>>.OnCompleted() => Console.WriteLine("DiagnosticListener was disposed!");
//  
//     void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
//
//     void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value) {
//         var context = new MessageContext(value.Value!, CancellationToken.None);
//         
//         Router
//             .Broadcast(Route.ForType(value.Value!.GetType()), context)
//             .GetAwaiter()
//             .GetResult();
//     }
//     
//     protected void On<T>(Func<T, InterceptorContext, Task> handler) where T : InterceptorEvent =>
//         Router.RegisterHandler<T, InterceptorContext>(
//             async (evt, ctx) => {
//                 //using var scope = ctx.Logger.BeginPropertyScope(LogProperties);
//                 try {
//                     await handler(evt, ctx);
//                 }
//                 catch (Exception ex) {
//                     ctx.Logger.LogWarning(ex, "{Interceptor} failed to intercept {EventName}", Name, typeof(T).Name);
//                 }
//             }
//         );
// 	
//     protected void On<T>(Action<T, InterceptorContext> handler) where T : InterceptorEvent =>
//         On<T>(handler.InvokeAsync);
//
//     public async ValueTask OnNextAsync(KeyValuePair<string, object?> value) => throw new NotImplementedException();
//
//     public async ValueTask OnErrorAsync(Exception error) => throw new NotImplementedException();
//
//     public async ValueTask OnCompletedAsync() => throw new NotImplementedException();
// }
//
// public static class DiagnosticListenerExtensions {
//     public static IDisposable SubscribeAsync<T>(this DiagnosticListener source, IAsyncObserver<KeyValuePair<string, object?>> observer) =>
//         source..Select(p => Observable.FromAsync(() => observer.OnNextAsync(p).AsTask())).Concat().Subscribe();
// }
//
// public class ProducerDiagnosticLogger {
//     public ProducerDiagnosticLogger() {
//         DiagnosticSource = new ProducerDiagnosticSource();
//         AsyncObserver.ToObserver()
//         Subscription    = DiagnosticSource.SubscribeAsync(new InterceptorEventObserver().To);
//     }
//
//     ProducerDiagnosticSource DiagnosticSource { get; }
//     IDisposable              Subscription     { get; }
//
//     public void Dispose() {
//         Subscription.Dispose();
//     }
// }
