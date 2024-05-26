namespace EventStore.Streaming.Schema.Serializers;

public class SerializationException(string message, Exception? innerException = null) : Exception(message, innerException);

public class SerializationFailedException(SchemaDefinitionType expectedSchemaType, SchemaInfo schemaInfo, Exception? innerException = null) 
	: SerializationException($"{expectedSchemaType} failed to serialize {schemaInfo.Subject}", innerException);

public class DeserializationFailedException(SchemaDefinitionType expectedSchemaType, SchemaInfo schemaInfo, Exception? innerException = null) 
	: SerializationException($"{expectedSchemaType} failed to deserialize {schemaInfo.Subject}", innerException);

public class UnsupportedSchemaException(SchemaDefinitionType expectedSchemaType, SchemaDefinitionType schemaType) 
	: SerializationException($"unsupported schema {schemaType} expected {expectedSchemaType}");

public class SerializerNotFoundException(SchemaDefinitionType schemaType, params SchemaDefinitionType[] supportedSchemaTypes) 
	: SerializationException($"unsupported schema {schemaType} expected one of {string.Join(", ", supportedSchemaTypes)}");
