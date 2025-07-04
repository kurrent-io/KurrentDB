namespace KurrentDB.SchemaRegistry.Domain;

public enum SchemaDataFormat {
    Unspecified = 0,
    Json        = 1, // content type: application/json
    Protobuf    = 2, // content type: application/vnd.google.protobuf
    Avro        = 3, // content type: application/vnd.apache.avro+json
    Bytes       = 4  // content type: application/octet-stream
}