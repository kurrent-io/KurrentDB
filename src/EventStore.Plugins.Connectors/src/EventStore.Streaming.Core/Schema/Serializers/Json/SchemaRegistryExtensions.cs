namespace EventStore.Streaming.Schema.Serializers.Json;

public static class SchemaRegistryExtensions {
	public static T UseJson<T>(this T registry, SystemJsonSerializerOptions? options = null) where T : SchemaRegistry =>
		(T)registry.RegisterSerializer(new SystemJsonSerializer(options, registry));
	
	public static async ValueTask RegisterJsonMessages<T>(this T registry, string? namespacePrefix = null, CancellationToken cancellationToken = default)
		where T : SchemaRegistry {
		foreach (var messageType in ScanMessages(namespacePrefix)) {
			var subject = SchemaSubjectNameStrategies.ForMessage(messageType).GetSubjectName();
			await registry.RegisterSchema(subject, SchemaDefinitionType.Json, "", messageType, cancellationToken);
		}
	}
	
	static List<Type> ScanMessages(string? namespacePrefix = null)  {
		var query = AssemblyScanner
			.ScanTypes()
			.Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false });

		if (!string.IsNullOrWhiteSpace(namespacePrefix))
			query = query.Where(type => type.Namespace != null && type.Namespace.StartsWith(namespacePrefix));

		var result = query.ToList();

		return result;
	}
}