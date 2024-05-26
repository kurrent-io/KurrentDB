using DotNext.Collections.Generic;

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

namespace EventStore.Streaming.Routing.Broadcasting;

public delegate Task BroadcastMessage(IMessageContext context, IEnumerable<IMessageHandler> handlers);

public interface IMessageBroadcaster {
	Task Broadcast(IMessageContext context, IEnumerable<IMessageHandler> handlers);
}

public record MessageBroadcaster(BroadcastMessage Strategy) : IMessageBroadcaster {
	static MessageBroadcaster() {
		SequentialBroadcaster   = new(static async (ctx, handlers) => await handlers.ForEachAsync(async (x, _) => await x.Handle(ctx)));
		ParallelBroadcaster     = new(static async (ctx, handlers) => await Parallel.ForEachAsync(handlers, async (x, _) => await x.Handle(ctx)));
		AsynchronousBroadcaster = new(static async (ctx, handlers) => await handlers.Select(x => x.Handle(ctx)).WhenAll());
	}
	
	static readonly MessageBroadcaster SequentialBroadcaster;
	static readonly MessageBroadcaster ParallelBroadcaster;
	static readonly MessageBroadcaster AsynchronousBroadcaster;
	
	public Task Broadcast(IMessageContext context, IEnumerable<IMessageHandler> handlers) => 
		Strategy(context, handlers);
	
	public static IMessageBroadcaster Get(MessageBroadcasterStrategy strategy) => strategy switch {
		MessageBroadcasterStrategy.Sequential => SequentialBroadcaster,
		MessageBroadcasterStrategy.Parallel   => ParallelBroadcaster,
		MessageBroadcasterStrategy.Async      => AsynchronousBroadcaster
	};
}
