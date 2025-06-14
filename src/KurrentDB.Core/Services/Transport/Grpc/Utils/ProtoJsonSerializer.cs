// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace KurrentDB.Core.Services.Transport.Grpc.Utils;

/// <summary>
/// A serializer class that supports serialization and deserialization of objects
/// with optional use of a protobuf-based formatter.
/// </summary>
class ProtoJsonSerializer(JsonSerializerOptions? options = null) {
	static readonly JsonParser ProtoJsonParser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

	static readonly Type MissingType = Type.Missing.GetType();

	public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new(JsonSerializerOptions.Default) {
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
			: ProtobufToUtf8JsonBytes(protoMessage);

		return bytes;

        static ReadOnlyMemory<byte> ProtobufToUtf8JsonBytes(IMessage message) {
            return Encoding.UTF8.GetBytes(
                RemoveWhitespacesExceptInQuotes(JsonFormatter.Default.Format(message))
            );

            // simply because protobuf is so stupid that it adds spaces
            // between property names and values. absurd...
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
			? !type.IsProtoMessage()
				? JsonSerializer.Deserialize(data.Span, type, Options)
				: ProtoJsonParser.Parse(Encoding.UTF8.GetString(data.Span.ToArray()), type.GetProtoMessageDescriptor())
			: JsonSerializer.Deserialize<JsonNode>(data.Span, Options);

		return value;
	}

	public async ValueTask<object?> Deserialize(Stream data, Type type, CancellationToken cancellationToken = default) {
		var value = type != MissingType
			? !type.IsProtoMessage()
				? await JsonSerializer.DeserializeAsync(data, type, Options, cancellationToken).ConfigureAwait(false)
				: ProtoJsonParser.Parse(await DeserializeToJson(data, cancellationToken).ConfigureAwait(false), type.GetProtoMessageDescriptor())
			: await JsonSerializer.DeserializeAsync<JsonNode>(data, Options, cancellationToken).ConfigureAwait(false);

		return value;

		static async Task<string> DeserializeToJson(Stream stream, CancellationToken cancellationToken) {
			using var reader = new StreamReader(stream, Encoding.UTF8);
			return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
