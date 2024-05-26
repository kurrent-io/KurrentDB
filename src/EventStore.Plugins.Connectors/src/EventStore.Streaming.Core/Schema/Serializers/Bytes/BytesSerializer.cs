namespace EventStore.Streaming.Schema.Serializers.Bytes;

[PublicAPI]
public class BytesSerializer : ISchemaSerializer {
	public SchemaDefinitionType SchemaType => SchemaDefinitionType.Bytes;

	public async ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SerializationContext context) {
		if (context.SchemaInfo.SchemaType != SchemaDefinitionType.Bytes)
			throw new UnsupportedSchemaException(SchemaType, context.SchemaInfo.SchemaType);
		
		if (value is null)
			return ReadOnlyMemory<byte>.Empty;
		
		var messageType = value.GetType();

		if (messageType == typeof(byte[]))
			return value.As<byte[]>();

		if (messageType == typeof(ReadOnlyMemory<byte>))
			return value.As<ReadOnlyMemory<byte>>();

		if (messageType == typeof(Memory<byte>))
			return value.As<Memory<byte>>();
		
		throw new UnsupportedSchemaException(SchemaType, context.SchemaInfo.SchemaType);
	}

	public async ValueTask<object?> Deserialize(ReadOnlyMemory<byte> data, SerializationContext context) {
		if (context.SchemaInfo.SchemaType != SchemaDefinitionType.Bytes)
			throw new UnsupportedSchemaException(SchemaType, context.SchemaInfo.SchemaType);

		return data;
	}
}