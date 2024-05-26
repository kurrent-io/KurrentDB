namespace EventStore.Streaming.Schema;

public enum SchemaDefinitionType {
	Undefined = 0,
	Json      = 1,
	Protobuf  = 2,
	//Avro      = 3,
	Bytes     = 4
}