// using Google.Protobuf;
// using KurrentDB.Surge.Testing.Messages.Telemetry;
// using KurrentDB.SchemaRegistry.Tests.Fixtures;
// using KurrentDB.Protocol.Registry.V2;
// using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
// using KurrentDB.SchemaRegistry.Services.Domain;
// using CompatibilityMode = KurrentDB.Protocol.V2.CompatibilityMode;
// using SchemaFormat = KurrentDB.Protocol.V2.SchemaFormat;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Domain;
//
// public class RegisterSchemaCommandTests : SchemaApplicationTestFixture {
//     [Test, Timeout(20_000)]
//     public async Task registers_initial_version_of_new_schema(CancellationToken cancellationToken) {
//         var command = new RegisterSchemaRequest {
//             SchemaName    = $"{nameof(PowerConsumption)}-{Faker.Random.AlphaNumeric(10)}",
//             Format        = SchemaFormat.Json,
//             Definition    = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
//             Compatibility = Faker.Random.Enum(CompatibilityMode.Undefined)
//         };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeTrue();
//
//         var ok = result.Get()!;
//         ok.Changes.Should().HaveCount(1);
//
//         var evt = ok.Changes.First().Event as SchemaRegistered;
//         evt.Should().NotBeNull();
//         evt.SchemaVersionId.Should().NotBeEmpty();
//         evt.SchemaName.Should().Be(command.SchemaName);
//         evt.Format.Should().Be(SchemaFormat.Json);
//         evt.VersionNumber.Should().Be(1);
//         evt.Definition.Should().BeEquivalentTo(command.Definition);
//         evt.Compatibility.Should().Be(command.Compatibility);
//         evt.RegisteredAt.Should().Be(TimeProvider.GetUtcNow());
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task registers_new_version_of_existing_schema(CancellationToken cancellationToken) {
//         var schemaName        = $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
//         var compatibilityMode = Faker.Random.Enum(CompatibilityMode.Undefined);
//
//         // Register initial version
//         await Apply(
//             new RegisterSchemaRequest {
//                 SchemaName    = schemaName,
//                 Format        = SchemaFormat.Json,
//                 Definition    = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
//                 Compatibility = compatibilityMode
//             },
//             cancellationToken
//         );
//
//         // Register the same schema again with an amended definition
//         var command = new RegisterSchemaRequest {
//             SchemaName    = schemaName,
//             Format        = SchemaFormat.Json,
//             Definition    = ByteString.CopyFromUtf8(Faker.Lorem.Text()),
//             Compatibility = compatibilityMode
//         };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeTrue();
//
//         var ok = result.Get()!;
//         ok.Changes.Should().HaveCount(1);
//
//         var evt = ok.Changes.First().Event as SchemaRegistered;
//         evt.Should().NotBeNull();
//         evt.SchemaVersionId.Should().NotBeEmpty();
//         evt.SchemaName.Should().Be(command.SchemaName);
//         evt.Format.Should().Be(SchemaFormat.Json);
//         evt.VersionNumber.Should().Be(2);
//         evt.Definition.Should().BeEquivalentTo(command.Definition);
//         evt.Compatibility.Should().Be(command.Compatibility);
//         evt.RegisteredAt.Should().Be(TimeProvider.GetUtcNow());
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_schema_definition_does_not_change(CancellationToken cancellationToken) {
//         var schemaName        = $"{nameof(PowerConsumption)}-{Faker.Random.AlphaNumeric(10)}";
//         var messageSchema     = ByteString.CopyFromUtf8(Faker.Lorem.Text());
//         var compatibilityMode = Faker.Random.Enum(CompatibilityMode.Undefined);
//
//         // Register initial version
//         await Apply(
//             new RegisterSchemaRequest {
//                 SchemaName    = schemaName,
//                 Format        = SchemaFormat.Json,
//                 Definition    = messageSchema,
//                 Compatibility = compatibilityMode
//             },
//             cancellationToken
//         );
//
//         // Try registering the same schema again with no changes
//         var result = await Apply(
//             new RegisterSchemaRequest {
//                 SchemaName    = schemaName,
//                 Format        = SchemaFormat.Json,
//                 Definition    = messageSchema,
//                 Compatibility = compatibilityMode
//             },
//             cancellationToken
//         );
//
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityAlreadyExists>();
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_schema_is_already_destroyed(CancellationToken cancellationToken) {
//         var schemaName        = $"{nameof(PowerConsumption)}-{Faker.Random.AlphaNumeric(10)}";
//         var messageSchema     = ByteString.CopyFromUtf8(Faker.Lorem.Text());
//         var compatibilityMode = Faker.Random.Enum(CompatibilityMode.Undefined);
//
//         // Register initial version
//         await Apply(
//             new RegisterSchemaRequest {
//                 SchemaName    = schemaName,
//                 Format        = SchemaFormat.Json,
//                 Definition    = messageSchema,
//                 Compatibility = compatibilityMode
//             },
//             cancellationToken
//         );
//
//         // Destroy the schema
//         var destroyResult = await Apply(new DestroySchemaRequest { SchemaName = schemaName }, cancellationToken);
//
//         destroyResult.Success.Should().BeTrue();
//
//         // Try registering a schema under the same name
//         var result = await Apply(
//             new RegisterSchemaRequest {
//                 SchemaName    = schemaName,
//                 Format        = SchemaFormat.Json,
//                 Definition    = messageSchema,
//                 Compatibility = compatibilityMode
//             },
//             cancellationToken
//         );
//
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityNotFound>();
//     }
// }
