using EventStore.Streaming.Producers;

namespace EventStore.Streaming.Schema.Serializers.Protobuf;

public static class MessageBuilderExtensions {
	public static MessageBuilder AsProtobuf(this MessageBuilder builder) =>
		builder.WithSchemaType(SchemaDefinitionType.Protobuf);
}