namespace KurrentDB.Api.Streams;

/// <summary>
/// Specifies the data format for schema content.
/// </summary>
public enum SchemaFormat {
    /// <summary>
    /// Default value indicating that the data format is unspecified.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The data is in JSON format.
    /// </summary>
    Json = 1,

    /// <summary>
    /// The data is in Protocol Buffers format.
    /// </summary>
    Protobuf = 2,

    /// <summary>
    /// The data is in Avro format.
    /// </summary>
    Avro = 3,

    /// <summary>
    /// The data is in raw bytes format.
    /// </summary>
    Bytes = 4
}
