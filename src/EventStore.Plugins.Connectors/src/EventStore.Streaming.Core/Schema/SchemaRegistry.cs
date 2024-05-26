using EventStore.Streaming.Schema.Serializers;
using EventStore.Streaming.Schema.Serializers.Bytes;
using EventStore.Streaming.Schema.Serializers.Json;
using EventStore.Streaming.Schema.Serializers.Protobuf;
using Google.Protobuf;

namespace EventStore.Streaming.Schema;

[PublicAPI]
public class SchemaRegistry : ISchemaSerializer {
	public static readonly SchemaRegistry Global      = new SchemaRegistry(new InMemorySchemaRegistryClient()).UseBytes().UseJson().UseProtobuf();
	public static readonly Type           MissingType = Type.Missing.GetType();

	public SchemaRegistry(ISchemaRegistryClient client, bool autoRegister = true) {
		Client       = client;
		AutoRegister = autoRegister;
	}

	public SchemaRegistry(bool autoRegister = true) : this(new InMemorySchemaRegistryClient(), autoRegister) { }

	ISchemaRegistryClient Client { get; }

	Dictionary<(string Subject, SchemaDefinitionType SchemaType), Type> MessageTypeMap { get; } = new();

	Dictionary<SchemaDefinitionType, ISchemaSerializer> Serializers { get; } = new();

	public bool AutoRegister { get; }

	public ValueTask<Type> ResolveMessageType(string subject, SchemaDefinitionType schemaType) =>
		ValueTask.FromResult(MessageTypeMap.GetValueOrDefault((Subject: subject, SchemaType: schemaType), MissingType));
	
	public ValueTask<Type> ResolveMessageType(SchemaInfo schemaInfo) =>
		ResolveMessageType(schemaInfo.Subject, schemaInfo.SchemaType);

	public async ValueTask<RegisteredSchema> RegisterSchema(string subject, SchemaDefinitionType schemaType, string definition, Type messageType, CancellationToken cancellationToken = default) {
        var request = new CreateOrUpdateSchema {
			Subject    = subject,
			SchemaType = (SchemaType) schemaType,
			Definition = !string.IsNullOrWhiteSpace(definition) ? ByteString.CopyFromUtf8(definition) : ByteString.Empty
		};

		var response = await Client.CreateOrUpdateSchema(request, cancellationToken);

		MessageTypeMap.TryAdd((subject, schemaType), messageType);

		return new RegisteredSchema {
			Subject    = subject,
			SchemaType = schemaType,
			Definition = definition,
			RevisionId = response.RevisionId,
			Version    = response.Version,
			CreatedAt  = response.CreatedAt.ToDateTimeOffset()
		};
	}
	
	public ValueTask<RegisteredSchema> RegisterSchema(SchemaInfo schemaInfo, Type messageType, CancellationToken cancellationToken = default) => 
		RegisterSchema(schemaInfo.Subject, schemaInfo.SchemaType, "", messageType, cancellationToken);

	public ValueTask<RegisteredSchema> RegisterSchema<T>(SchemaInfo schemaInfo, CancellationToken cancellationToken = default) => 
		RegisterSchema(schemaInfo, typeof(T), cancellationToken);
	
	public ValueTask<RegisteredSchema> RegisterSchema<T>(string subject, SchemaDefinitionType schemaType, CancellationToken cancellationToken = default) => 
		RegisterSchema<T>(new SchemaInfo(subject, schemaType), cancellationToken);
	
	public ValueTask<RegisteredSchema> RegisterSchema<T>(SchemaDefinitionType schemaType, CancellationToken cancellationToken = default) => 
		RegisterSchema(SchemaInfo.FromMessageType<T>(schemaType), typeof(T), cancellationToken);
	
	public async ValueTask<RegisteredSchema> GetLatestSchema(string subject, SchemaDefinitionType schemaType, CancellationToken cancellationToken = default) {
		var request = new GetLatestSchema {
			Subject    = subject,
			SchemaType = (SchemaType) schemaType
		};
		
		var response = await Client.GetLatestSchema(request, cancellationToken);

		if (response.Schema == null)
			return RegisteredSchema.None;
		
		return new RegisteredSchema {
			Subject    = subject,
			SchemaType = schemaType,
			Definition = response.Schema.Definition.ToStringUtf8(),
			RevisionId = response.Schema.RevisionId,
			Version    = response.Schema.Version,
			CreatedAt  = response.Schema.CreatedAt.ToDateTimeOffset()
		};
	}
	
	public ValueTask<RegisteredSchema> GetLatestSchema<T>(SchemaDefinitionType schemaType, CancellationToken cancellationToken = default) =>
		GetLatestSchema(SchemaInfo.FromMessageType<T>(schemaType).Subject, schemaType, cancellationToken);
	
	public async Task<List<RegisteredSchema>> ListMessageSchemas(Type messageType, SchemaDefinitionType schemaType = SchemaDefinitionType.Undefined) {
		var result = MessageTypeMap.Where(x => x.Value == messageType && (schemaType == SchemaDefinitionType.Undefined || x.Key.SchemaType == schemaType))
			.Select(x => new SchemaInfo(x.Key.Subject, x.Key.SchemaType))
			.ToList();

		var schemas = new List<RegisteredSchema>();
		
		foreach (var schemaInfo in result) {
			var schema = await GetLatestSchema(schemaInfo.Subject, schemaInfo.SchemaType);
			schemas.Add(schema);
		}
		
		return schemas;
	}

	public Task<List<RegisteredSchema>> ListMessageSchemas<T>(SchemaDefinitionType schemaType = SchemaDefinitionType.Undefined) =>
		ListMessageSchemas(typeof(T), schemaType);
	
	#region . Serialization .

	public SchemaRegistry RegisterSerializer(ISchemaSerializer serializer) {
		Serializers[serializer.SchemaType] = serializer;
		return this;
	}

	public ISchemaSerializer GetSerializer(SchemaDefinitionType schemaType) => Serializers[schemaType]; // I know...

	// public ISchemaSerializer GetSerializer(MessageSchema messageSchema) => GetSerializer(messageSchema.Type);

	public bool SupportsSchema(SchemaDefinitionType schemaType) => Serializers.ContainsKey(schemaType);

	#endregion . Serialization .

	#region . ISchemaSerializer .

	SchemaDefinitionType ISchemaSerializer.SchemaType => SchemaDefinitionType.Undefined;

	public async ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SerializationContext context) {
		if (Serializers.TryGetValue(context.SchemaInfo.SchemaType, out var serializer))
			return await serializer.Serialize(value, context);

		throw new SerializerNotFoundException(context.SchemaInfo.SchemaType, Serializers.Keys.ToArray());
	}

	public async ValueTask<object?> Deserialize(ReadOnlyMemory<byte> data, SerializationContext context) {
		if (Serializers.TryGetValue(context.SchemaInfo.SchemaType, out var serializer))
			return await serializer.Deserialize(data, context);

		throw new SerializerNotFoundException(context.SchemaInfo.SchemaType, Serializers.Keys.ToArray());
	}

	#endregion . ISchemaSerializer .
}