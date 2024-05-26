// ReSharper disable CheckNamespace

namespace EventStore.IO.Connectors;

/// <summary>
/// very experimental. needs to be aligned also with instrumentation tags...
/// </summary>
public static class SinkHeaderKeys {
    public const string RequestDate = "ESDB-Request-Date";

    public const string RecordId           = "ESDB-Record-Id";
    public const string RecordPartitionKey = "ESDB-Record-Partition-Key";
    public const string RecordTimestamp    = "ESDB-Record-Timestamp";
    public const string RecordIsRedacted   = "ESDB-Record-Is-Redacted";

    public const string StreamId       = "ESDB-Stream-Id";
    public const string StreamRevision = "ESDB-Stream-Revision";

    public const string LogPosition    = "ESDB-Log-Position";
    public const string RecordPosition = "ESDB-Record-Position";

    public const string SchemaSubject = "ESDB-Schema-Subject";
    public const string SchemaType    = "ESDB-Schema-Type";

    public const string CustomMetadataPrefix = "ESDB-Metadata-";
}