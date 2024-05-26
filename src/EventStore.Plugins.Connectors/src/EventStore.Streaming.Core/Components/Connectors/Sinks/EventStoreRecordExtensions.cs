// ReSharper disable CheckNamespace

using EventStore.Streaming;

namespace EventStore.IO.Connectors;

public static class EventStoreRecordExtensions {
	
    /// <summary>
    /// very experimental. needs to be aligned also with instrumentation tags...
    /// </summary>
    public static void InjectDefaultHeaders(this EventStoreRecord record, Action<string, string?> addHeader) {
		addHeader(SinkHeaderKeys.RecordId, $"{record.Id}");
		addHeader(SinkHeaderKeys.RecordPartitionKey, $"{record.Key}");
		addHeader(SinkHeaderKeys.RecordTimestamp, $"{record.Timestamp}");
		addHeader(SinkHeaderKeys.RecordIsRedacted, $"{record.IsRedacted}");
		
		addHeader(SinkHeaderKeys.StreamId, record.StreamId);
		addHeader(SinkHeaderKeys.StreamRevision, $"{record.Position.StreamRevision}");
		
		addHeader(SinkHeaderKeys.LogPosition, $"{record.Position.LogPosition.CommitPosition}");
		
		addHeader(SinkHeaderKeys.SchemaSubject, record.SchemaInfo.Subject);
		addHeader(SinkHeaderKeys.SchemaType, record.SchemaInfo.SchemaType.ToString().ToLower());
		
		foreach (var (key, value) in record.Headers)
			addHeader($"{SinkHeaderKeys.CustomMetadataPrefix}{key}", value);
	}
}