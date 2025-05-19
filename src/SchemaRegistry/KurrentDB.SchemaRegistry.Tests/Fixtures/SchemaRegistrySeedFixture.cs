// using Bogus;
// using Google.Protobuf;
// using KurrentDB.Surge.Testing.Messages.Telemetry;
// using KurrentDB.Protocol.Registry.V2;
// using static KurrentDB.Protocol.V2.SchemaRegistryService;
//
// namespace KurrentDB.SchemaRegistry.Tests.Fixtures;
//
// public class SchemaRegistrySeedFixture(Faker faker, SchemaRegistryServiceClient client) {
//     Faker                       Faker  { get; } = faker;
//     SchemaRegistryServiceClient Client { get; } = client;
//
//     public async ValueTask<RegisteredSchema> CreateSchema(
//         string? name = null,
//         ByteString? definition = null,
//         CompatibilityMode? compatibility = null,
//         CancellationToken cancellationToken = default
//     ) {
//         var schemaName       = name       ?? $"{nameof(PowerConsumption)}-{Identifiers.GenerateShortId()}";
//         var schemaDefinition = definition ?? ByteString.CopyFromUtf8(Faker.Lorem.Text());
//
//         var command = new CreateSchemaRequest {
//             SchemaName       = schemaName,
//             SchemaDefinition = schemaDefinition,
//             DataFormat       = SchemaFormat.Json,
//             Compatibility    = compatibility ?? CompatibilityMode.Backward
//         };
//
//         var response = await Client.CreateSchemaAsync(command, cancellationToken: cancellationToken);
//
//         await WaitForSchemaRegisteredProjected(response.SchemaVersionId, cancellationToken);
//
//         return new RegisteredSchema {
//             SchemaVersionId  = response.SchemaVersionId,
//             VersionNumber    = response.VersionNumber,
//             SchemaDefinition = command.SchemaDefinition,
//             SchemaName       = command.SchemaName,
//             DataFormat       = command.DataFormat,
//             Compatibility    = command.Compatibility,
//             Tags             = { command.Tags },
//             RegisteredAt     = response.CreatedAt
//         };
//     }
//
//     public async ValueTask DeleteSchemaVersion(string id, CancellationToken cancellationToken = default) {
//         await Client.DeleteSchemaVersionAsync(new() { SchemaVersionId = id }, cancellationToken: cancellationToken);
//         await WaitForSchemaDeleteProjected(id, cancellationToken);
//     }
//
//     public async ValueTask DestroySchema(string name, CancellationToken cancellationToken = default) {
//         await Client.DestroySchemaAsync(new() { SchemaName = name }, cancellationToken: cancellationToken);
//         await WaitForSchemaDestroyProjected(name, cancellationToken);
//     }
//
//     async ValueTask WaitForSchemaRegisteredProjected(string id, CancellationToken cancellationToken) {
//         await Wait.UntilAsserted(
//             async () => await Client.GetSchemaVersionAsync(new() { SchemaVersionId = id }, cancellationToken: cancellationToken),
//             cancellationToken: cancellationToken
//         );
//     }
//
//     async ValueTask WaitForSchemaDeleteProjected(string id, CancellationToken cancellationToken) {
//         await Wait.UntilAsserted(
//             async () => {
//                 var response = await Client.GetSchemaVersionAsync(new() { SchemaVersionId = id }, cancellationToken: cancellationToken);
//                 response.Schema.IsDeleted.Should().BeTrue();
//             },
//             cancellationToken: cancellationToken
//         );
//     }
//
//     async ValueTask WaitForSchemaDestroyProjected(string name, CancellationToken cancellationToken) {
//         await Wait.UntilAsserted(
//             async () => {
//                 var response = await Client.ListSchemasAsync(new() { NamePrefix = name }, cancellationToken: cancellationToken);
//                 response.Schemas.Should().NotContain(x => x.Name == name);
//             },
//             cancellationToken: cancellationToken
//         );
//     }
// }