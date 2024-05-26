using EventStore.Streaming.Producers;

namespace EventStore.Streaming.Schema.Serializers.Json;

public static class MessageBuilderExtensions {
	public static MessageBuilder AsJson(this MessageBuilder builder) =>
		builder.WithSchemaType(SchemaDefinitionType.Json);
}