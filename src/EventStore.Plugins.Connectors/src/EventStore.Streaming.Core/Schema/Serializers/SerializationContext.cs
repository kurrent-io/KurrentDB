namespace EventStore.Streaming.Schema.Serializers;

/// <summary>
/// It allows the serialization operations to be autonomous.
/// </summary>
[PublicAPI]
public readonly record struct SerializationContext(Headers Headers) {
	/// <summary>
	/// The headers present in the record.
	/// </summary>
	public Headers Headers { get; } = Headers;

	/// <summary>
	/// The schema type extracted from the headers. Undefined if not present.
	/// </summary>
	public SchemaInfo SchemaInfo { get; } = SchemaInfo.FromHeaders(Headers);
	
	public static SerializationContext From(Headers headers) => 
		new(headers);
	
	public static SerializationContext From(SchemaInfo schemaInfo) => 
		From(new Headers().WithSchemaInfo(schemaInfo));
}