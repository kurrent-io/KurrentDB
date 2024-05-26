using System.Net.Http.Headers;

namespace EventStore.Streaming.Schema;

[PublicAPI]
public record SchemaInfo(string Subject, SchemaDefinitionType SchemaType) {
	static readonly MediaTypeHeaderValue JsonContentTypeHeader     = new("application/json");
	static readonly MediaTypeHeaderValue ProtobufContentTypeHeader = new("application/vnd.google.protobuf");
	static readonly MediaTypeHeaderValue AvroContentTypeHeader     = new("application/vnd.apache.avro+json");
	static readonly MediaTypeHeaderValue BytesContentTypeHeader    = new("application/octet-stream");

	public static readonly SchemaInfo None = new("", SchemaDefinitionType.Undefined);

	public MediaTypeHeaderValue ContentTypeHeader { get; } = SchemaType switch {
		SchemaDefinitionType.Json     => JsonContentTypeHeader,
		SchemaDefinitionType.Protobuf => ProtobufContentTypeHeader,
		_                             => BytesContentTypeHeader,
	};

	public string ContentType => ContentTypeHeader.MediaType!;

	public SchemaInfo InjectIntoHeaders(Headers headers) {
		headers.Set(HeaderKeys.SchemaSubject, Subject);
		headers.Set(HeaderKeys.SchemaType, SchemaType.ToString().ToLower());
		return this;
	}

	public static SchemaInfo FromHeaders(Headers headers) {
		return new(ExtractSchemaSubject(headers), ExtractSchemaType(headers));

		static string ExtractSchemaSubject(Headers headers) =>
			headers.TryGetValue(HeaderKeys.SchemaSubject, out var subject) ? subject! : "";

		static SchemaDefinitionType ExtractSchemaType(Headers headers) {
			return headers.TryGetValue(HeaderKeys.SchemaType, out var value)
			    && Enum.TryParse<SchemaDefinitionType>(value, true, out var schemaType)
				       ? schemaType
				       : SchemaDefinitionType.Undefined;
		}
	}

	/// <summary>
	/// For legacy purposes, we need to be able to create the schema info from the content type.
	/// </summary>
	public static SchemaInfo FromContentType(string subject, string contentType) {
		Ensure.NotNullOrEmpty(subject);
		Ensure.NotNullOrEmpty(contentType);

		var schemaType = contentType == JsonContentTypeHeader.MediaType
			? SchemaDefinitionType.Json
			: contentType == ProtobufContentTypeHeader.MediaType
				? SchemaDefinitionType.Protobuf
				: contentType == BytesContentTypeHeader.MediaType
					? SchemaDefinitionType.Bytes
					: SchemaDefinitionType.Undefined;

		return new(subject, schemaType);
	}

	public static SchemaInfo FromMessageType(Type messageType, SchemaDefinitionType schemaType) {
		var subject = SchemaSubjectNameStrategies
			.ForMessage(messageType)
			.GetSubjectName();

		return new(subject, schemaType);
	}

	public static SchemaInfo FromMessageType<T>(SchemaDefinitionType schemaType) =>
		FromMessageType(typeof(T), schemaType);

	public static SchemaInfo FromMessage(object message, SchemaDefinitionType schemaType) =>
		FromMessageType(message.GetType(), schemaType);
}