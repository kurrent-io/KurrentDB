// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace KurrentDB.Core.Services.Transport.Grpc.V2.Utils;

/// <summary>
/// Serializes and deserializes objects to and from JSON with special handling for Protobuf messages.
/// </summary>
/// <param name="options">
/// Optional <see cref="JsonSerializerOptions"/> to customize serialization behavior.
/// </param>
public class ProtoJsonSerializer(JsonSerializerOptions? options = null) {
	public static readonly ProtoJsonSerializer Default = new();

	static readonly ProtobufMessages Messages = new();
	static readonly JsonParser ProtoJsonParser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

	static readonly Type MissingType = Type.Missing.GetType();

	static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new(JsonSerializerOptions.Default) {
		PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
		DictionaryKeyPolicy         = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = false,
		DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
		UnknownTypeHandling         = JsonUnknownTypeHandling.JsonNode,
		UnmappedMemberHandling      = JsonUnmappedMemberHandling.Skip,
		NumberHandling              = JsonNumberHandling.AllowReadingFromString,
		Converters                  = {
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
		}
	};

	JsonSerializerOptions Options { get; } = options ?? DefaultJsonSerializerOptions;

	public ReadOnlyMemory<byte> Serialize(object? value) {
		var bytes = value is not IMessage protoMessage
			? JsonSerializer.SerializeToUtf8Bytes(value, Options)
			: SerializeProtoToJson(protoMessage);

		return bytes;

        static ReadOnlyMemory<byte> SerializeProtoToJson(IMessage message) {
            return Encoding.UTF8.GetBytes(
                RemoveWhitespacesExceptInQuotes(JsonFormatter.Default.Format(message))
            );

            // simply because protobuf adds spaces between property names and values.
            static string RemoveWhitespacesExceptInQuotes(string json) {
                var inQuotes = false;
                var result   = new StringBuilder(json.Length);

                foreach (var c in json) {
                    if (c == '\"') {
                        inQuotes = !inQuotes;
                        result.Append(c); // Always include the quote characters
                    } else if (inQuotes || (!inQuotes && !char.IsWhiteSpace(c)))
                        result.Append(c);
                }

                return result.ToString();
            }
        }
	}

	public object? Deserialize(ReadOnlyMemory<byte> data, Type type) {
		var value = type != MissingType
			? !typeof(IMessage).IsAssignableFrom(type)
				? JsonSerializer.Deserialize(data.Span, type, Options)
				: DeserializeJsonToProto(data, type)
			: JsonSerializer.Deserialize<JsonNode>(data.Span, Options);

		return value;

		static object? DeserializeJsonToProto(ReadOnlyMemory<byte> jsonData, Type type) {
			return ProtoJsonParser.Parse(
				Encoding.UTF8.GetString(jsonData.Span.ToArray()),
				Messages.GetDescriptor(type)
			);
		}
	}

	class ProtobufMessages {
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
}
