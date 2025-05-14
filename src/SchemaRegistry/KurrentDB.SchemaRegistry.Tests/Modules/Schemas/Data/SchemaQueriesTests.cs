using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Testing.Messages.Telemetry;
using KurrentDB.SchemaRegistry.Tests.Fixtures;
using KurrentDB.Protocol.Registry.V2;

namespace KurrentDB.SchemaRegistry.Tests.Schemas.Data;

public class SchemaQueriesTests : SchemaRegistryServerTestFixture {
    [Test, Timeout(10_000)]
    public async Task get_schema_version_with_version_number_returns_version(CancellationToken cancellationToken) {
        // Arrange
        var schemaName = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";

        var schemaDefinition = Faker.Lorem.Sentences(10, Environment.NewLine);

        var createSchemaCommand = new CreateSchemaRequest {
            SchemaName       = schemaName,
            SchemaDefinition = ByteString.CopyFromUtf8(schemaDefinition),
            Details = new SchemaDetails {
                Description   = Faker.Lorem.Text(),
                DataFormat    = SchemaDataFormat.Json,
                Compatibility = Faker.Random.Enum(CompatibilityMode.Unspecified),
                Tags = {
                    new Dictionary<string, string> {
                        [Faker.Lorem.Word()] = Faker.Lorem.Word(),
                        [Faker.Lorem.Word()] = Faker.Lorem.Word(),
                        [Faker.Lorem.Word()] = Faker.Lorem.Word()
                    }
                }
            }
        };

        var createSchemaResponse = await Client.CreateSchemaAsync(createSchemaCommand, cancellationToken: cancellationToken);

        var expectedResponse = new GetSchemaVersionResponse {
            Version = new SchemaVersion {
                SchemaVersionId  = createSchemaResponse.SchemaVersionId,
                SchemaDefinition = createSchemaCommand.SchemaDefinition,
                DataFormat       = createSchemaCommand.Details.DataFormat,
                VersionNumber    = createSchemaResponse.VersionNumber,
                RegisteredAt     = Timestamp.FromDateTime(TimeProvider.GetUtcNow().UtcDateTime)
            }
        };

        // Act
        // await Wait.UntilAsserted(
        //     async () => {
        //         var response = await Client.GetSchemaVersionAsync(
        //             new GetSchemaVersionRequest {
        //                 SchemaName    = schemaName,
        //                 VersionNumber = createSchemaResponse.VersionNumber
        //             },
        //             cancellationToken: cancellationToken
        //         );
        //
        //         response.Should().BeEquivalentTo(expectedResponse);
        //     },
        //     cancellationToken: cancellationToken
        // );

        // await Tasks.SafeDelay(1_000, cancellationToken);

        var response = await Client.GetSchemaVersionAsync(
            new GetSchemaVersionRequest {
                SchemaName    = schemaName,
                VersionNumber = createSchemaResponse.VersionNumber
            },
            cancellationToken: cancellationToken
        );

        // Assert
        // WARNING!!! BECAUSE for some reason, FLUENT ASSERTIONS it is not using the options!!!
        expectedResponse.Version.RegisteredAt = response.Version.RegisteredAt;

        response.Should().BeEquivalentTo(
            expectedResponse, options => options
                .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, 1.Seconds()))
                .WhenTypeIs<DateTime>()
        );
    }
}