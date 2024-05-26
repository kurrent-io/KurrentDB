namespace EventStore.Streaming.Schema;

public record RegisteredSchema {
	public static readonly RegisteredSchema None = new();
	
	public string               Subject    { get; init; }
	public SchemaDefinitionType SchemaType { get; init; }
	public string               RevisionId { get; init; }
	public string               Definition { get; init; }
	public int                  Version    { get; init; }
	public DateTimeOffset       CreatedAt  { get; init; }
}