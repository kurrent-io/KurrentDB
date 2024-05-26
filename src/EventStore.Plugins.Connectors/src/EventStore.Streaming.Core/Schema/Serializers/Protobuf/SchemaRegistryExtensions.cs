using EventStore.Streaming.Schema.Serializers.Json;

namespace EventStore.Streaming.Schema.Serializers.Protobuf;

public static class SchemaRegistryExtensions {
	public static T UseProtobuf<T>(this T registry) where T : SchemaRegistry =>
		(T)registry.RegisterSerializer(new ProtobufSerializer(registry));

	public static async ValueTask RegisterProtobufMessages<T>(this T registry, string? namespacePrefix = null, CancellationToken cancellationToken = default)
		where T : SchemaRegistry {
		foreach (var messageType in ScanMessages(namespacePrefix)) {
			var subject = SchemaSubjectNameStrategies.ForMessage(messageType).GetSubjectName();
			await registry.RegisterSchema(subject, SchemaDefinitionType.Protobuf, "", messageType, cancellationToken);
		}
	}
	
	static List<Type> ScanMessages(string? namespacePrefix = null)  {
		var query = AssemblyScanner
			.ScanTypes()
			.Where(type => type.IsProtoMessage());

		if (!string.IsNullOrWhiteSpace(namespacePrefix))
			query = query.Where(type => type.Namespace != null && type.Namespace.StartsWith(namespacePrefix));

		return query.ToList();
	}
}