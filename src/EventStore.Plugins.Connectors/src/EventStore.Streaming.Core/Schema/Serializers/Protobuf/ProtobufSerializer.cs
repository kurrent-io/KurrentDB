using System.Collections.Concurrent;
using Google.Protobuf;

namespace EventStore.Streaming.Schema.Serializers.Protobuf;

public class ProtobufSerializer : SerializerBase {
	public override SchemaDefinitionType SchemaType => SchemaDefinitionType.Protobuf;

	public ProtobufSerializer(SchemaRegistry? registry = null) : base(registry) {
		Parsers   = new();
		GetParser = type => Parsers.GetOrAdd(type, static msgType => msgType.MessageParser());
	}
	
	ConcurrentDictionary<Type, MessageParser> Parsers   { get; }
	GetMessageParser                          GetParser { get; }

	protected override ValueTask<ReadOnlyMemory<byte>> SerializeValue(object? value, SerializationContext context) => 
		new(value.EnsureValueIsProtoMessage().ToByteArray());

	protected override ValueTask<object?> DeserializeData(ReadOnlyMemory<byte> data, Type resolvedType, SerializationContext context) {
		if (resolvedType.IsMissing())
			throw new($"Type could not be resolved from schema name: {context.SchemaInfo.Subject}");

		var result = GetParser(resolvedType.EnsureTypeIsProtoMessage()).ParseFrom(data.Span);

		return new(result);
	}

	delegate MessageParser GetMessageParser(Type messageType);
}