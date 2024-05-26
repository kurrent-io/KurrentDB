using EventStore.Streaming.Consumers.LifecycleEvents;
using EventStore.Streaming.Interceptors;

namespace EventStore.Streaming.Consumers.Interceptors;

[PublicAPI]
class ConsumerLogger : InterceptorModule {
	public ConsumerLogger(string? name = null) : base(name) {
		On<RecordReceived>(
			(evt, ctx) => {
				ctx.Logger.LogTrace(
					"{ConsumerName} {EventName} {RecordId} {MessageType} {Stream}",
					evt.Consumer.ConsumerName,
					nameof(RecordReceived),
					evt.Record.Id,
					evt.Record.SchemaInfo.Subject,
					evt.Record.StreamId
				);
			}
		);

		On<RecordTracked>(
			(evt, ctx) => {
				ctx.Logger.LogTrace(
					"{ConsumerName} {EventName} {RecordId} {MessageType} {Stream}",
					evt.Consumer.ConsumerName,
					nameof(RecordTracked),
					evt.Record.Id,
					evt.Record.SchemaInfo.Subject,
					evt.Record.StreamId
				);
			}
		);

		On<PartitionEndReached>(
			(evt, ctx) => {
				ctx.Logger.LogDebug(
					"{ConsumerName} {EventName} {StreamId} |> [{Partition}] @ {LogPosition}",
					evt.Consumer.ConsumerName,
					nameof(PartitionEndReached),
					evt.Position.StreamId,
					evt.Position.PartitionId,
					evt.Position.LogPosition.CommitPosition
				);
			}
		);

		On<PositionsCommitted>(
			(evt, ctx) => {
				if (evt.Positions.Any()) {

					var lastPosition = evt.Positions.Max(x => x.LogPosition);
					
					ctx.Logger.LogInformation(
						"{ConsumerName} {EventName} ({PositionsCount}) >> [{LastPosition}]",
						evt.Consumer.ConsumerName,
						nameof(PositionsCommitted),
						evt.Positions.LongCount(),
						lastPosition.CommitPosition
					);
					
					//
					// var positionsByStream = evt.Positions
					// 	.OrderBy(x => x.StreamPosition.Stream)
					// 	.ThenBy(x => x.PartitionId)
					// 	.GroupBy(x => x.StreamPosition.Stream);
					//
					// // one position per partition...
					//
					// foreach (var positions in positionsByStream) {
					// 	Logger.LogInformation(
					// 		"{ConsumerName} {EventName} {Stream} >> ({PositionsCount}) {Positions}",
					// 		evt.Consumer.ConsumerName,
					// 		nameof(PositionsCommitted),
					// 		positions.Key,
					// 		positions.LongCount(),
					// 		positions.Select(x => $"{x.PartitionId}@{x.LogPosition.CommitPosition}")
					// 	);
					// }
				}
				else {
					ctx.Logger.LogDebug(
						"{ConsumerName} {EventName} ({PositionsCount})",
						evt.Consumer.ConsumerName,
						nameof(PositionsCommitted),
						0
					);
				}
			}
		);

		On<ConsumerUnsubscribed>(
			(evt, ctx) => {
				if (evt.Error is null)
					ctx.Logger.LogDebug(
						"{ConsumerName} {EventName}",
						evt.Consumer.ConsumerName,
						nameof(ConsumerUnsubscribed)
					);
				else
					ctx.Logger.LogError(
						evt.Error,
						"{ConsumerName} {EventName} {ErrorMessage}",
						evt.Consumer.ConsumerName,
						nameof(ConsumerUnsubscribed),
						evt.Error.Message
					);
			}
		);
		
		On<ConsumerStopped>(
			(evt, ctx) => {
				if (evt.Error is null)
					ctx.Logger.LogDebug(
						"{ConsumerName} {EventName}",
						evt.Consumer.ConsumerName,
						nameof(ConsumerStopped)
					);
				else
					ctx.Logger.LogError(
						evt.Error,
						"{ConsumerName} {EventName} {ErrorMessage}",
						evt.Consumer.ConsumerName,
						nameof(ConsumerStopped),
						evt.Error.Message
					);
			}
		);
	}
}