namespace EventStore.Streaming.Schema.Serializers;

// TODO SS: rethink the interface having into account the schema registry influence and the schema context as helper
public interface ISchemaSerializer {
	public SchemaDefinitionType SchemaType { get; }
	
	public ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SerializationContext context);
	public ValueTask<object?>              Deserialize(ReadOnlyMemory<byte> data, SerializationContext context);

	public ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, Headers headers) =>
		Serialize(value, SerializationContext.From(headers));
	
	public ValueTask<object?> Deserialize(ReadOnlyMemory<byte> data, Headers headers) =>
		Deserialize(data, SerializationContext.From(headers));

	public ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SchemaInfo schemaInfo) =>
		Serialize(value, SerializationContext.From(schemaInfo));
	
	public ValueTask<object?> Deserialize(ReadOnlyMemory<byte> data, SchemaInfo schemaInfo) =>
		Deserialize(data,SerializationContext.From(schemaInfo) );
}

public delegate ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, Headers headers);

public delegate ValueTask<object?> Deserialize(ReadOnlyMemory<byte> data, Headers headers);