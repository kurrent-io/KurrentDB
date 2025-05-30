using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

using Contracts = KurrentDB.Protocol.Registry.V2;

namespace KurrentDB.SchemaRegistry.Data;

public static class SchemaQueriesMapping {
	public static Contracts.SchemaValidationInfo MapToSchemaValidationInfo(dynamic source) =>
		new() {
			SchemaVersionId = source.version_id,
			SchemaDefinition = source.schema_definition is ByteString bs
				? bs.ToStringUtf8()
				: ByteString.CopyFromUtf8(source.schema_definition),
			DataFormat = (Contracts.SchemaDataFormat)source.data_format,
			Compatibility = (Contracts.CompatibilityMode)source.compatibility
		};

    public static MapField<string, string?> MapToTags(string source) =>
        source == "{}" ? new() : new() { JsonSerializer.Deserialize<Dictionary<string, string?>>(source) };

    public static ByteString MapToSchemaDefinition(string? source) =>
        source is null || source.Length == 0 ? ByteString.Empty : ByteString.CopyFromUtf8(source);

    public static Contracts.SchemaDetails MapToSchemaDetails(dynamic source) =>
        new() {
            Description   = source.description,
            DataFormat    = (Contracts.SchemaDataFormat)source.data_format,
            Compatibility = (Contracts.CompatibilityMode)source.compatibility,
            Tags          = { MapToTags(source.tags) }
        };

    public static Contracts.Schema MapToSchema(dynamic source) =>
        new() {
            SchemaName          = source.schema_name,
            Details             = MapToSchemaDetails(source),
            LatestSchemaVersion = source.latest_version_number,
            CreatedAt           = MapToTimestamp(source.created_at),
            UpdatedAt           = MapToTimestamp(source.updated_at)
        };

    public static Contracts.SchemaVersion MapToSchemaVersion(dynamic source) =>
        new() {
            SchemaVersionId  = source.version_id,
            SchemaDefinition = MapToSchemaDefinition(source.schema_definition),
            DataFormat       = (Contracts.SchemaDataFormat)source.data_format,
            VersionNumber    = source.version_number,
            RegisteredAt     = MapToTimestamp(source.registered_at)
        };

    public static Contracts.RegisteredSchema MapToRegisteredSchema(dynamic source) =>
        new() {
            SchemaName       = source.schema_name,
            DataFormat       = (Contracts.SchemaDataFormat)source.data_format,
            Compatibility    = (Contracts.CompatibilityMode)source.compatibility,
            Tags             = { MapToTags(source.tags) },
            SchemaVersionId  = source.version_id,
            SchemaDefinition = MapToSchemaDefinition(source.schema_definition),
            VersionNumber    = source.version_number,
            RegisteredAt     = MapToTimestamp(source.registered_at),
        };

    public static Contracts.SchemaCompatibilityResult MapToSchemaCompatibilityResult(Kurrent.Surge.Schema.Validation.SchemaCompatibilityResult result, string schemaVersionId) {
        return new () {
            IsCompatible    = result.IsCompatible,
            SchemaVersionId = schemaVersionId,
            Errors          = { result.Errors.Select(MapToSchemaValidationError) }
        };

        static Contracts.SchemaCompatibilityError MapToSchemaValidationError(Kurrent.Surge.Schema.Validation.SchemaCompatibilityError value) =>
            new() {
                Kind         = (Contracts.SchemaCompatibilityErrorKind)value.Kind,
                Details      = value.Details,
                PropertyPath = value.PropertyPath,
                OriginalType = value.OriginalType.ToString(),
                NewType      = value.NewType.ToString()
            };
    }

    static Timestamp MapToTimestamp(DateTime dateTime) =>
        Timestamp.FromDateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
}
