using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;
using CompatibilityMode = KurrentDB.Protocol.Registry.V2.CompatibilityMode;
using SchemaFormat = KurrentDB.Protocol.Registry.V2.SchemaDataFormat;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Domain;

public class CreateSchemaCommandTests : SchemaApplicationTestFixture {
    [Test, Timeout(20_000)]
    public async Task registers_initial_version_of_new_schema(CancellationToken cancellationToken) {
        // Arrange
        var expectedEvent = new SchemaCreated {
            SchemaName       = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}",
            SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
            Description      = Faker.Lorem.Text(),
            DataFormat       = SchemaDataFormat.Json,
            Compatibility    = Faker.Random.Enum(CompatibilityMode.Unspecified),
            Tags = {
                new Dictionary<string, string> {
                    [Faker.Lorem.Word()] = Faker.Lorem.Word(),
                    [Faker.Lorem.Word()] = Faker.Lorem.Word(),
                    [Faker.Lorem.Word()] = Faker.Lorem.Word()
                }
            },
            SchemaVersionId = Guid.NewGuid().ToString(),
            VersionNumber   = 1,
            CreatedAt       = Timestamp.FromDateTimeOffset(TimeProvider.GetUtcNow())
        };

        // Act
        var result = await Apply(
            new CreateSchemaRequest {
                SchemaName       = expectedEvent.SchemaName,
                SchemaDefinition = expectedEvent.SchemaDefinition,
                Details = new SchemaDetails {
                    Description   = expectedEvent.Description,
                    DataFormat    = expectedEvent.DataFormat,
                    Compatibility = expectedEvent.Compatibility,
                    Tags          = { expectedEvent.Tags }
                }
            },
            cancellationToken
        );

        // Assert
        var schemaCreated = result.Changes.Should().HaveCount(1).And.Subject.GetSingleEvent<SchemaCreated>()
            // WARNING!!! BECAUSE for some reason, FLUENT ASSERTIONS it is not ignoring the SchemaVersionId
            .With(x => x.SchemaVersionId = expectedEvent.SchemaVersionId);

        schemaCreated.Should().BeEquivalentTo(expectedEvent, o => o.Excluding(e => e.SchemaVersionId));
    }

    [Test, Timeout(20_000)]
    public async Task throws_exception_when_schema_is_deleted(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

        // given the schema is created
        await Apply(
            new CreateSchemaRequest {
                SchemaName       = schemaName,
                SchemaDefinition = ByteString.CopyFromUtf8(Faker.Lorem.Sentences()),
                Details = new SchemaDetails {
                    Description   = Faker.Lorem.Text(),
                    DataFormat    = SchemaFormat.Json,
                    Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
                    Tags = {
                        new Dictionary<string, string> {
                            [Faker.Lorem.Word()] = Faker.Lorem.Word(),
                            [Faker.Lorem.Word()] = Faker.Lorem.Word(),
                            [Faker.Lorem.Word()] = Faker.Lorem.Word()
                        }
                    }
                }
            },
            cancellationToken
        );

        // given the schema is deleted
        await Apply(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken);

        // Act
        var deleteSchema = async () => await Apply(new DeleteSchemaRequest { SchemaName = schemaName }, cancellationToken);

        // Assert
        await deleteSchema.ShouldThrowAsync<DomainExceptions.EntityNotFound>();
    }
}
