// ReSharper disable UnusedAutoPropertyAccessor.Global

using System.Collections.Immutable;
using Eventuous;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;

namespace KurrentDB.SchemaRegistry.Domain;

using Versions = ImmutableDictionary<string, SchemaEntity.SchemaVersion>;

[PublicAPI]
public record SchemaEntity : State<SchemaEntity> {
    public SchemaEntity() {
        On<SchemaCreated>((state, evt) => state with {
            SchemaName    = evt.SchemaName,
            Description   = evt.Description,
            DataFormat    = (SchemaDataFormat)evt.DataFormat,
            Compatibility = (CompatibilityMode)evt.Compatibility,
            Tags          = evt.Tags,
            Versions      = state.Versions.Add(evt.SchemaVersionId, new () {
                SchemaVersionId       = evt.SchemaVersionId,
                SchemaDefinitionBytes = evt.SchemaDefinition.Memory,
                SchemaDefinition      = evt.SchemaDefinition.ToStringUtf8(),
                VersionNumber         = evt.VersionNumber,
                RegisteredAt          = evt.CreatedAt.ToDateTimeOffset()
            })
        });

        On<SchemaCompatibilityModeChanged>((state, evt) => state with {
            Compatibility = (CompatibilityMode)evt.Compatibility
        });

        On<SchemaVersionRegistered>((state, evt) => state with {
            Versions = state.Versions.Add(evt.SchemaVersionId, new () {
                SchemaVersionId       = evt.SchemaVersionId,
                VersionNumber         = evt.VersionNumber,
                SchemaDefinitionBytes = evt.SchemaDefinition.Memory,
                SchemaDefinition      = evt.SchemaDefinition.ToStringUtf8(),
                RegisteredAt          = evt.RegisteredAt.ToDateTimeOffset()
            })
        });

        On<SchemaVersionsDeleted>((state, evt) => state with {
            Versions = state.Versions.RemoveRange(evt.Versions)
        });

        On<SchemaDeleted>((state, evt) => state with {
            DeletedAt = evt.DeletedAt.ToDateTimeOffset()
        });
    }

    public string                      SchemaName    { get; init; } = "";
    public string                      Description   { get; init; } = "";
    public SchemaDataFormat            DataFormat    { get; init; } = SchemaDataFormat.Unspecified;
    public CompatibilityMode           Compatibility { get; init; } = CompatibilityMode.Unspecified;
    public IDictionary<string, string> Tags          { get; init; } = new Dictionary<string, string>();
    public Versions                    Versions      { get; init; } = Versions.Empty;
    public DateTimeOffset              DeletedAt     { get; init; } = DateTimeOffset.MinValue;

    public bool IsNew => SchemaName == "";

    public bool IsDeleted =>
        DeletedAt != DateTimeOffset.MinValue;

    public SchemaVersion LatestVersion => Versions.Values.LastOrDefault() ?? SchemaVersion.None;

    public SchemaVersion GetVersion(string id) {
        return Versions.TryGetValue(id, out var schemaVersion)
            ? schemaVersion
            : throw new DomainExceptions.EntityNotFound("SchemaVersion", id);
    }

    public SchemaEntity EnsureVersionIsNotTheLast(string schemaVersionId) {
        if (Versions.Values.Count() == 1)
            throw new DomainExceptions.EntityException($"SchemaVersion {schemaVersionId} cannot be deleted because it is the last version of schema {SchemaName}");

        return this;
    }

    public SchemaEntity EnsureNotDeleted() {
        if (IsDeleted)
            throw new DomainExceptions.EntityNotFound("Schema", $"schemas/{SchemaName}");

        return this;
    }

    public record SchemaVersion {
        public static readonly SchemaVersion None = new SchemaVersion {
            SchemaVersionId       = Guid.Empty.ToString(),
            VersionNumber         = 0,
            SchemaDefinitionBytes = default,
            SchemaDefinition      = "",
            RegisteredAt          = default
        };

        public required string               SchemaVersionId       { get; init; }
        public required int                  VersionNumber         { get; init; }
        public required ReadOnlyMemory<byte> SchemaDefinitionBytes { get; init; }
        public required string               SchemaDefinition      { get; init; }
        public required DateTimeOffset       RegisteredAt          { get; init; }

        // public DateTimeOffset DeletedAt { get; init; } = DateTimeOffset.MinValue;
        //
        // public bool IsDeleted =>
        //     DeletedAt != DateTimeOffset.MinValue;

        public bool IsSameDefinition(ReadOnlyMemory<byte> otherDefinition) =>
            !SchemaDefinitionBytes.IsEmpty && SchemaDefinitionBytes.Span.SequenceEqual(otherDefinition.Span);
    }
}