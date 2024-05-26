namespace EventStore.Streaming.Schema.Serializers;

public abstract class SerializerBase(SchemaRegistry? registry = null) : ISchemaSerializer {
	SchemaRegistry Registry { get; } = registry ?? SchemaRegistry.Global;
	
	public abstract SchemaDefinitionType SchemaType { get; }
	
	async ValueTask<ReadOnlyMemory<byte>> ISchemaSerializer.Serialize(object? value, SerializationContext context) {
		// TODO SS: should we simply return empty byte[] if the value is null, or should we continue with the schema validation?
		if (value is null)
			return ReadOnlyMemory<byte>.Empty;
		
		if (context.SchemaInfo.SchemaType != SchemaType)
			throw new UnsupportedSchemaException(SchemaType, context.SchemaInfo.SchemaType);

		var messageType = value.GetType();
		
		var schemaInfo = SchemaInfo.FromMessageType(messageType, SchemaType);
		
		var schema = await Registry.GetLatestSchema(schemaInfo.Subject, schemaInfo.SchemaType);
		if (schema == RegisteredSchema.None) {
			if (!Registry.AutoRegister)
				throw new Exception(
					$"The message schema for {messageType.FullName} is not registered and auto registration is disabled. " +
				    $"Please register the schema or enable auto registration."
				);
			
			await Registry.RegisterSchema(schemaInfo.Subject, schemaInfo.SchemaType, "", messageType);
		}
		
		schemaInfo.InjectIntoHeaders(context.Headers);
		
		try {
			return await SerializeValue(value, context);
		}
		catch (Exception ex) {
			throw new SerializationFailedException(SchemaType, schemaInfo,  ex);
		}
	}
	
	async ValueTask<object?> ISchemaSerializer.Deserialize(ReadOnlyMemory<byte> data, SerializationContext context) {
		// TODO SS: should we simply return null if the data is empty, or should we continue with the schema validation?
		if (data.IsEmpty)
			return null;
		
		// TODO SS: should we bypass the schema registry when schema info indicates that the message type is bytes?
		if (context.SchemaInfo.SchemaType == SchemaDefinitionType.Bytes)
			return data;

		if (context.SchemaInfo.SchemaType != SchemaType)
			throw new UnsupportedSchemaException(SchemaType, context.SchemaInfo.SchemaType);
		
		// TODO SS: the schema registry should be used to validate the message schema before deserializing
		
		var messageType = await Registry.ResolveMessageType(context.SchemaInfo.Subject, context.SchemaInfo.SchemaType);

		try {
			return await DeserializeData(data, messageType, context);
		}
		catch (Exception ex) {
			throw new DeserializationFailedException(SchemaType, new SchemaInfo(context.SchemaInfo.Subject, context.SchemaInfo.SchemaType),  ex);
		}
	}

	protected abstract ValueTask<ReadOnlyMemory<byte>> SerializeValue(object? value, SerializationContext context);

	protected abstract ValueTask<object?> DeserializeData(ReadOnlyMemory<byte> data, Type resolvedType, SerializationContext context);
}