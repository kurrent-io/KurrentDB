using EventStore.Streaming.Consumers;
using EventStore.Streaming.Interceptors;
using EventStore.Streaming.Producers;
using EventStore.Streaming.Schema.Serializers.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Messages = EventStore.Streaming.Contracts.Processors;
	
namespace EventStore.Streaming.Processors.Interceptors;

[PublicAPI]
class ProcessorStateChangePublisher : InterceptorModule {
	public ProcessorStateChangePublisher(IProducer producer, string? name = null) : base(name) {
		On<ProcessorStateChanged>(
			async (evt, ctx) => {
				try {
					var message = Message.Builder.AsProtobuf()
						.Value(evt.MapProcessorStateChanged())
						.Key(evt.Processor.ProcessorId)
						.Create();
					
					await producer.Send(message);
					
					ctx.Logger.LogTrace(
						"Published {ProcessorId} state change from {FromState} to {State}", 
						evt.Processor.ProcessorId, evt.FromState, evt.ToState
					);
				}
				catch (Exception ex) {
					ctx.Logger.LogWarning(
						ex, "Failed to publish {ProcessorId} state change from {FromState} to {State}", 
						evt.Processor.ProcessorId, evt.FromState, evt.ToState
					);
				}
			}
		);
	}
}

[PublicAPI]
public static class ProcessorContractMaps {
	public static Messages.ProcessorState MapProcessorState(this ProcessorState source) =>
		(Messages.ProcessorState)source;
	
	public static Messages.ConsumeFilter MapConsumeFilter(this ConsumeFilter source) =>
		new() {
			Scope  = (Messages.ConsumeFilterScope)source.Scope,
			Filter = source.RegularExpression.ToString()
		};
	
	public static Messages.ProcessorMetadata MapProcessorMetadata(this IProcessorMetadata source) =>
		new() {
            ProcessorId      = source.ProcessorId,
            ProcessorName    = source.ProcessorName,
			SubscriptionName = source.SubscriptionName,
			State            = source.State.MapProcessorState(),
			Filter           = source.Filter.MapConsumeFilter()
		};

	public static Messages.EventMetadata MapEventMetadata(this InterceptorEvent source) =>
		new() {
			EventId       = source.Id.ToString(),
			EventSeverity = (Messages.EventSeverity)source.Severity.GetHashCode(),
			Timestamp     = Timestamp.FromDateTimeOffset(source.Timestamp)
		};
		
	public static Messages.ErrorDetails? MapErrorDetails(this Exception? source) =>
		source is null ? null : new() {
			Code         = source.GetType().Name,
			ErrorMessage = source.Message
		};

	public static Messages.ProcessorStateChanged MapProcessorStateChanged(this ProcessorStateChanged source) =>
		new() {
			Processor = source.Processor.MapProcessorMetadata(),
			Metadata  = source.MapEventMetadata(),
			Error     = source.Error.MapErrorDetails(),
			FromState = source.FromState.MapProcessorState(),
			ToState   = source.ToState.MapProcessorState()
		};
}