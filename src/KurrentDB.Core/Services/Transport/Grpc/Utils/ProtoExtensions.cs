#nullable enable

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace KurrentDB.Core.Services.Transport.Grpc.Utils;

static class ProtoExtensions {
    public static MessageParser GetProtoMessageParser(this Type messageType) =>
        ProtobufMessages.System.GetParser(messageType);

    public static MessageDescriptor GetProtoMessageDescriptor(this Type messageType) =>
        ProtobufMessages.System.GetDescriptor(messageType);

    public static Type EnsureTypeIsProtoMessage(this Type messageType) =>
	    !typeof(IMessage).IsAssignableFrom(messageType)
		    ? throw new InvalidCastException($"Type {messageType.Name} is not a Protocol Buffers message")
		    : messageType;

    public static bool IsProtoMessage(this Type messageType) =>
        typeof(IMessage).IsAssignableFrom(messageType);

    public static IMessage EnsureValueIsProtoMessage(this object? value) =>
	    value as IMessage ?? throw new InvalidOperationException($"Value of type {value!.GetType().Name} is not a Protocol Buffers message");
}

class ProtobufMessages {
    public static ProtobufMessages System { get; } = new();

    ConcurrentDictionary<Type, (MessageParser Parser, MessageDescriptor Descriptor)> Types { get; } = new();

    public MessageParser GetParser(Type messageType) =>
        Types.GetOrAdd(messageType, GetContext).Parser;

    public MessageDescriptor GetDescriptor(Type messageType) =>
        Types.GetOrAdd(messageType, GetContext).Descriptor;

    static (MessageParser Parser, MessageDescriptor Descriptor) GetContext(Type messageType) {
        return (GetMessageParser(messageType), GetMessageDescriptor(messageType));

        static MessageParser GetMessageParser(Type messageType) =>
            (MessageParser)messageType
                .GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;

        static MessageDescriptor GetMessageDescriptor(Type messageType) =>
            (MessageDescriptor)messageType
                .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
    }
}
