using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Producers.LifecycleEvents;

namespace EventStore.Streaming.Producers.Interceptors;

public sealed class ProducerLogger : InterceptorModule {
	public ProducerLogger(string? name = null) : base(name) {
		On<SendRequestReceived>(
			(evt, ctx) => {
				ctx.Logger.LogTrace(
					"{ProducerName} {EventName} {RequestId} ({MessageCount} msgs) >> {Stream}",
					evt.Producer.ProducerName,
					nameof(SendRequestReceived),
					evt.Request.RequestId,
					evt.Request.Messages.Count,
					evt.Request.Stream
				);

				var counter = 1;
				foreach (var message in evt.Request.Messages) {
					ctx.Logger.LogTrace(
						"{ProducerName} {EventName} {RequestId} {Order:000} {MessageType} {SchemaType} {RecordId} >> {Stream}",
						evt.Producer.ProducerName,
						nameof(SendRequestReceived),
						evt.Request.RequestId,
						counter++,
						message.Value.GetType().FullName!,
						message.SchemaType,
						message.RecordId.Value,
						evt.Request.Stream
					);
				}
			}
		);

		On<SendRequestProcessed>(
			(evt, ctx) => {
				if (evt.Result.Success) {
					ctx.Logger.LogDebug(
						"{ProducerName} {EventName} {RequestId} ({MessageCount} msgs) >> {Stream} @ {LogPosition}",
						evt.Producer.ProducerName,
						nameof(SendRequestSucceeded),
						evt.Result.RequestId,
						evt.Result.Messages.Count,
						evt.Result.Stream,
						evt.Result.Position.LogPosition.CommitPosition
					);
				}
				else {
					ctx.Logger.Log(
						evt.Result.Error!.IsCritical ? LogLevel.Critical : LogLevel.Error,
						"{ProducerName} {EventName} {RequestId} ({MessageCount} msgs) >> {Stream} {ErrorMessage}",
						evt.Producer.ProducerName,
						nameof(SendRequestFailed),
						evt.Result.RequestId,
						evt.Result.Messages.Count,
						evt.Result.Stream,
						evt.Result.Error!.Message
					);
				}
			}
		);
		
		On<SendRequestSucceeded>(
			(evt, ctx) => {
				ctx.Logger.LogDebug(
					"{ProducerName} {EventName} {RequestId} ({MessageCount} msgs) >> {StreamId} @ {LogPosition}",
					evt.Producer.ProducerName,
					nameof(SendRequestSucceeded),
					evt.Request.RequestId,
					evt.Request.Messages.Count,
					evt.Position.StreamId,
					evt.Position.LogPosition.CommitPosition
				);
				
				// foreach (var message in evt.Request.Messages) {
				// 	ctx.Logger.LogTrace(
				// 		"{ProducerName} {EventName} {RequestId} {MessageType} {RecordId} >> {StreamId} @ {LogPosition}",
				// 		evt.Producer.ProducerName,
				// 		nameof(SendRequestSucceeded),
				// 		evt.Request.RequestId,
				// 		message.Value.GetType().Name,
				// 		message.RecordId.Value,
				// 		evt.Position.StreamId,
				// 		evt.Position.LogPosition.CommitPosition
				// 	);
				// }
			}
		);
		
		On<SendRequestFailed>(
			(evt, ctx) => {
				ctx.Logger.Log(
					evt.Error.IsCritical ? LogLevel.Critical : LogLevel.Error,
					evt.Error,
					"{ProducerName} {EventName} {RequestId} {ErrorMessage}",
					evt.Producer.ProducerName,
					nameof(SendRequestFailed),
					evt.Request.RequestId,
					evt.Error.Message
				);
			}
		);

		On<SendRequestCallbackError>(
			(evt, ctx) => {
				ctx.Logger.LogError(
					evt.Error,
					"{ProducerName} {EventName} {RequestId} {ErrorMessage}",
					evt.Producer.ProducerName,
					nameof(SendRequestCallbackError),
					evt.Result.RequestId,
					evt.Error.Message
				);
			}
		);
		
		On<ProducerFlushed>(
			(evt, ctx) => {
				var logLevel = evt.Inflight > 0
					? LogLevel.Debug
					: LogLevel.Trace;
				
				ctx.Logger.Log(
					logLevel,
					"{ProducerName} {EventName} {HandledRequests}/{TotalRequests} requests",
					evt.Producer.ProducerName,
					nameof(ProducerFlushed),
					evt.Handled,
					evt.Inflight + evt.Handled
				);
			}
		);
		
		On<ProducerStopped>(
			(evt, ctx) => {
				if (evt.Error is null)
					ctx.Logger.LogDebug(
						"{ProducerName} {EventName}",
						evt.Producer.ProducerName,
						nameof(ProducerStopped)
					);
				else
					ctx.Logger.LogError(
						evt.Error,
						"{ProducerName} {EventName} {ErrorMessage}",
						evt.Producer.ProducerName,
						nameof(ProducerStopped),
						evt.Error.Message
					);
			}
		);
	}
}