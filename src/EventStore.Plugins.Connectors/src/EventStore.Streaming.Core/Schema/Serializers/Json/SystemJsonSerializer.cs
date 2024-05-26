using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Text.Json.JsonSerializer;

namespace EventStore.Streaming.Schema.Serializers.Json;

[PublicAPI]
public record SystemJsonSerializerOptions(JsonSerializerOptions JsonSerializerOptions) {
	public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new() {
		PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
		Converters             = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public SystemJsonSerializerOptions() : this(DefaultJsonSerializerOptions) { }
}

public class SystemJsonSerializer(SystemJsonSerializerOptions? options = null, SchemaRegistry? registry = null) : SerializerBase(registry) {
	SystemJsonSerializerOptions SerializerOptions { get; } = options ?? new SystemJsonSerializerOptions();

	public override SchemaDefinitionType SchemaType => SchemaDefinitionType.Json;

	protected override ValueTask<ReadOnlyMemory<byte>> SerializeValue(object? value, SerializationContext context) => 
		new(SerializeToUtf8Bytes(value, SerializerOptions.JsonSerializerOptions));

	protected override ValueTask<object?> DeserializeData(ReadOnlyMemory<byte> data, Type resolvedType, SerializationContext context) =>
		new(!resolvedType.IsMissing()
			? Deserialize(data.Span, resolvedType, SerializerOptions.JsonSerializerOptions)
			: JsonDocument.Parse(data));
}