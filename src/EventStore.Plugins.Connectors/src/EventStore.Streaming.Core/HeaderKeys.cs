namespace EventStore.Streaming;

public static class HeaderKeys {
	public const string ProducerName       = "esdb.producer.name";
	public const string ProducerRequestId  = "esdb.producer.request-id";
	public const string ProducerSequenceId = "esdb.producer.sequence-id";

	public const string SchemaSubject = "esdb.schema.subject";
	public const string SchemaType    = "esdb.schema.type";
	
	public const string PartitionKey  = "esdb.partition-key";
}