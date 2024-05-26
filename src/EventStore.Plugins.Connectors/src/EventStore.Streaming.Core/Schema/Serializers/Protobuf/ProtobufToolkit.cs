using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EventStore.Streaming.Schema.Serializers.Protobuf;

static class ProtobufToolkit {
	public static MessageParser MessageParser(this Type messageType) =>
		(MessageParser) messageType
			.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)!
			.GetValue(null)!;
	
	public static MessageDescriptor MessageDescriptor(this Type messageType) =>
		(MessageDescriptor) messageType
			.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!
			.GetValue(null)!;
	
	public static MessageDescriptor Proto(this Type messageType) =>
		(MessageDescriptor) messageType
			.GetProperty("Descriptor.Proto", BindingFlags.NonPublic | BindingFlags.Static)!
			.GetValue(null)!;

	public static Type EnsureTypeIsProtoMessage(this Type messageType) {
		if (!typeof(IMessage).IsAssignableFrom(messageType))
			throw new InvalidCastException($"Type {messageType.Name} is not a Protocol Buffers message");

		return messageType;
	}
	
	public static bool IsProtoMessage(this Type messageType) => 
		typeof(IMessage).IsAssignableFrom(messageType);

	public static IMessage EnsureValueIsProtoMessage(this object? value) {
		if (value is not IMessage message)
			throw new InvalidOperationException($"Value of type {value!.GetType().Name} is not a Protocol Buffers message");

		return message;
	}
}