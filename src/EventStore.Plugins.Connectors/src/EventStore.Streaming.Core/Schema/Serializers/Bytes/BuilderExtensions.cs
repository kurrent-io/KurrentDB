using EventStore.Streaming.Producers;

namespace EventStore.Streaming.Schema.Serializers.Bytes;

public static class MessageBuilderExtensions {
	public static MessageBuilder AsBytes(this MessageBuilder builder) =>
		builder.WithSchemaType(SchemaDefinitionType.Bytes);
}

public static class SchemaRegistryExtensions {
	public static T UseBytes<T>(this T registry) 
		where T : SchemaRegistry =>
		(T) registry.RegisterSerializer(new BytesSerializer());
}
