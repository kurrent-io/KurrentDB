namespace EventStore.Streaming.Schema;

public interface ISchemaSubjectNameStrategy {
	string GetSubjectName();
}

public class MessageStrategy(Type messageType) : ISchemaSubjectNameStrategy {
	public string GetSubjectName() => messageType.FullName!;
}

public class StreamMessageStrategy(StreamId stream, Type messageType) : ISchemaSubjectNameStrategy {
	public string GetSubjectName() => $"{stream}-{messageType.FullName!}";
}

public static class SchemaSubjectNameStrategies {
	public static ISchemaSubjectNameStrategy ForMessage(Type messageType) => 
		new MessageStrategy(messageType);
	
	public static ISchemaSubjectNameStrategy ForStreamMessage(StreamId stream, Type messageType) => 
		new StreamMessageStrategy(stream, messageType);
}
