using System.Collections.Concurrent;
using System.Text;
using System.Text.Unicode;
using Google.Protobuf;

namespace EventStore.Streaming.Schema.Serializers.Protobuf;

public class ProtobufJsonSerializer : SerializerBase {
	public override SchemaDefinitionType SchemaType => SchemaDefinitionType.Json;

	public ProtobufJsonSerializer(SchemaRegistry? registry = null) : base(registry) {
		Parsers   = new();
		GetParser = type => Parsers.GetOrAdd(type, static msgType => msgType.MessageParser());
	}

	ConcurrentDictionary<Type, MessageParser> Parsers   { get; }
	GetMessageParser                          GetParser { get; }

	protected override async ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SerializationContext context) {
        var json = JsonFormatter.Default.Format(value.EnsureValueIsProtoMessage());
        return Encoding.UTF8.GetBytes(json);
    }

    protected override ValueTask<object?> Deserialize(ReadOnlyMemory<byte> data, Type resolvedType, SerializationContext context) {
		if (resolvedType.IsMissing())
			throw new($"Type could not be resolved from schema name: {context.SchemaInfo.Subject}");

		var result = GetParser(resolvedType.EnsureTypeIsProtoMessage())
            .ParseJson(Encoding.UTF8.GetString(data.Span));

		return new(result);
	}

	delegate MessageParser GetMessageParser(Type messageType);
}