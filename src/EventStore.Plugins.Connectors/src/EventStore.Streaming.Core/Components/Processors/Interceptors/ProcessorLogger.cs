#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault

using System.Collections.Concurrent;
using System.Diagnostics;
using EventStore.Streaming.Interceptors;

namespace EventStore.Streaming.Processors.Interceptors;

[PublicAPI]
class ProcessorLogger : InterceptorModule {
	public ProcessorLogger(string? name = null) : base(name) {
		// just redirect until I can make up my mind
		On<ProcessorStateChanged>(
			async (stateChanged, ctx) => {
				ctx.Logger.LogInformation(
					"{ProcessorName} {EventName} {FromState} >>> {State}", 
					stateChanged.Processor.ProcessorName, nameof(ProcessorStateChanged), stateChanged.FromState, stateChanged.ToState
				);
				
				ctx.Logger.LogInformation(
					"{ProcessorName} {State}", 
					stateChanged.Processor.ProcessorName, stateChanged.ToState
				);
				
				// TODO SS: cant decide yet if one single processor state change event or many specific ones...
				ProcessorLifecycleEvent evt = stateChanged.Processor.State switch {
					ProcessorState.Activating   => new ProcessorActivating(stateChanged.Processor),
					ProcessorState.Running      => new ProcessorRunning(stateChanged.Processor),
					ProcessorState.Suspended    => new ProcessorSuspended(stateChanged.Processor),
					ProcessorState.Deactivating => new ProcessorDeactivating(stateChanged.Processor),
					ProcessorState.Stopped      => new ProcessorStopped(stateChanged.Processor, stateChanged.Error),
				};

				await Intercept(ctx with { Message = evt });
			}
		);
		
		On<ProcessorActivating>(
		    (evt, ctx) => {
			    ctx.Logger.LogInformation(
			        "{ProcessorName} {EventName} subscribing to {Streams} as member of {SubscriptionName}", 
			        evt.Processor.ProcessorName, nameof(ProcessorActivating), evt.Processor.Streams, evt.Processor.SubscriptionName
		        );
                
		        foreach (var endpoint in evt.Processor.Endpoints)
			        ctx.Logger.LogInformation(
                        "{MessageKey} subscribed by {Handlers}", 
                        endpoint.Route, 
                        endpoint.Handlers.Select(OwnerTypeName).ToArray()
                    );

		        return;

		        static string OwnerTypeName(string handlerName) => 
                    handlerName.Contains("ProcessingModule") ? "Handler" : handlerName;
		    }
		);

		On<ProcessorRunning>(
			(evt, ctx) => {
				ctx.Logger.LogInformation("{ProcessorName} {EventName}", evt.Processor.ProcessorName, nameof(ProcessorRunning));
			}
		);
		
		On<ProcessorSuspended>(
			(evt, ctx) => {
				ctx.Logger.LogInformation("{ProcessorName} {EventName}", evt.Processor.ProcessorName, nameof(ProcessorSuspended));
			}
		);

		On<ProcessorDeactivating>(
			(evt, ctx) => {
			    if (evt.Error is null)
				    ctx.Logger.LogDebug("{ProcessorName} {EventName}", evt.Processor.ProcessorName, nameof(ProcessorDeactivating));
			    else
				    ctx.Logger.LogWarning(evt.Error, "{ProcessorName} {EventName} {ErrorMessage}", evt.Processor.ProcessorName, nameof(ProcessorDeactivating), evt.Error.Message);
		    }
		);

		On<ProcessorStopped>(
			(evt, ctx) => {
				if (evt.Error is null)
					ctx.Logger.LogInformation(
						"{ProcessorName} {EventName}",
						evt.Processor.ProcessorName,
						nameof(ProcessorStopped)
					);
				else
					ctx.Logger.LogError(
						evt.Error,
						"{ProcessorName} {EventName} {ErrorMessage}",
						evt.Processor.ProcessorName,
						nameof(ProcessorStopped),
						evt.Error.Message
					);
			}
		);

		On<RecordSkipped>(
			(evt, ctx) => {
				ctx.Logger.LogTrace(
					"{ProcessorName} {EventName} {Stream} {RecordId} {MessageType}",
					evt.Processor.ProcessorName,
					nameof(RecordSkipped),
					evt.Record.StreamId,
					evt.Record.Id,
					evt.Record.SchemaInfo.Subject
				);
			}
		);

		var inProcessRecords = new ConcurrentDictionary<RecordId, Stopwatch>();

		On<RecordReady>(
			(evt, ctx) => {
				inProcessRecords.TryAdd(evt.Record.Id, Stopwatch.StartNew());

				ctx.Logger.LogTrace(
					"{ProcessorName} {EventName} {Stream} {RecordId} {MessageType}",
					evt.Processor.ProcessorName,
					nameof(RecordReady),
					evt.Record.StreamId,
					evt.Record.Id,
					evt.Record.SchemaInfo.Subject
				);
			}
		);

		On<RecordHandled>(
			(evt, ctx) => {
		        inProcessRecords.TryGetValue(evt.Record.Id, out var stopwatch);

		        ctx.Logger.LogDebug(
			        "{ProcessorName} {EventName} {RecordId} {MessageType} {Stream} ({OutputCount} output) {Elapsed}",
			        evt.Processor.ProcessorName,
			        nameof(RecordHandled),
			        evt.Record.StreamId,
			        evt.Record.Id,
			        evt.Record.SchemaInfo.Subject,
			        evt.Requests.Sum(x => x.Messages.Count),
			        stopwatch!.Elapsed
		        );
		    }
		);
		
		On<RecordProcessed>(
			(evt, ctx) => {
				inProcessRecords.TryRemove(evt.Record.Id, out var stopwatch);

				stopwatch!.Stop();

				if (evt.Results.Count > 0) {
					ctx.Logger.LogDebug(
						"{ProcessorName} {EventName} {Stream} {RecordId} {MessageType} ({OutputCount} output) {Elapsed}",
						evt.Processor.ProcessorName,
						nameof(RecordProcessed),
						evt.Record.StreamId,
						evt.Record.Id,
						evt.Record.SchemaInfo.Subject,
						evt.Results.Sum(x => x.Messages.Count),
						stopwatch.Elapsed
					);
				}
			}
		);
				
		On<RecordProcessingError>(
			(evt, ctx) => {
				inProcessRecords.TryRemove(evt.Record.Id, out var stopwatch);

				stopwatch!.Stop();

				ctx.Logger.LogError(
					"{ProcessorName} {EventName} {Stream} {RecordId} {MessageType} {ErrorMessage}",
					evt.Processor.ProcessorName,
					nameof(RecordProcessingError),
					evt.Record.SchemaInfo.Subject,
					evt.Record.Id,
					evt.Record.StreamId,
					evt.Error!.Message
				);
			}
		);

		
	}
}