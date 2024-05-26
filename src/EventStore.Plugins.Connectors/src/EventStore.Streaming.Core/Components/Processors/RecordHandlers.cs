using EventStore.Streaming.Routing;

namespace EventStore.Streaming.Processors;

public interface IRecordHandler : IMessageHandler {
    public Task Process(RecordContext context);
    
    Task IMessageHandler.Handle(IMessageContext context) => Process((RecordContext)context);
}

[PublicAPI]
public abstract class RecordHandler : IRecordHandler {
    public abstract Task Process(RecordContext context);
    
    internal class Proxy(ProcessRecord handler) : RecordHandler {
        public override Task Process(RecordContext context) => handler(context);
    }
}

[PublicAPI]
public abstract class RecordHandler<TMessage> : IRecordHandler {
    public abstract Task Process(TMessage message, RecordContext context);
    
    Task IRecordHandler.Process(RecordContext context) => Process((TMessage)context.Message, context);
    
    internal class Proxy(ProcessRecord<TMessage> handler) : RecordHandler<TMessage> {
        public override Task Process(TMessage message, RecordContext context) => handler(message, context);
    }
}

[PublicAPI]
public abstract class SynchronousRecordHandler : IRecordHandler {
	public abstract void Process(RecordContext context);

    Task IRecordHandler.Process(RecordContext context) {
        Process(context);
        return Task.CompletedTask;
    }
    
    internal class Proxy(ProcessRecordSynchronously handler) : SynchronousRecordHandler {
        public override void Process(RecordContext context) => handler(context);
    }
}

[PublicAPI]
public abstract class SynchronousRecordHandler<TMessage> : IRecordHandler {
    public abstract void Process(TMessage message, RecordContext context);

    Task IRecordHandler.Process(RecordContext context) {
        Process((TMessage)context.Message, context);
        return Task.CompletedTask;
    }
    
    internal class Proxy(ProcessRecordSynchronously<TMessage> handler) : SynchronousRecordHandler<TMessage> {
        public override void Process(TMessage message, RecordContext context) => handler(message, context);
    }
}

public delegate Task ProcessRecord(RecordContext context);
public delegate Task ProcessRecord<in T>(T message, RecordContext context);

public delegate void ProcessRecordSynchronously(RecordContext context);
public delegate void ProcessRecordSynchronously<in T>(T message, RecordContext context);