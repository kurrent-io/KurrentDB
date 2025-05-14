// using KurrentDB.SchemaRegistry.Tests.Fixtures;
// using KurrentDB.Protocol.Registry.V2;
// using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
// using KurrentDB.SchemaRegistry.Services.Domain;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Domain;
//
// public class ChangeSchemaCompatibilityModeCommandTests : SchemaApplicationTestFixture {
//     [Test, Timeout(20_000)]
//     public async Task change_schema_compatibility_mode(CancellationToken cancellationToken) {
//         var originalCompatibility = Faker.Random.Enum(CompatibilityMode.Undefined);
//         var (_, name) = await Register(compatibility: originalCompatibility, cancellationToken: cancellationToken);
//
//         var command = new ChangeSchemaCompatibilityModeRequest {
//             SchemaName    = name,
//             Compatibility = Faker.Random.Enum(originalCompatibility, CompatibilityMode.Undefined)
//         };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeTrue();
//
//         var ok = result.Get()!;
//         ok.Changes.Should().HaveCount(1);
//
//         var evt = ok.Changes.First().Event as SchemaCompatibilityModeChanged;
//         evt.Should().NotBeNull();
//
//         evt.SchemaName.Should().Be(name);
//         evt.Compatibility.Should().Be(command.Compatibility);
//         evt.ChangedAt.Should().Be(TimeProvider.GetUtcNow());
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_compatibility_does_not_change(CancellationToken cancellationToken) {
//         var originalCompatibility = Faker.Random.Enum(CompatibilityMode.Undefined);
//         var (_, name) = await Register(compatibility: originalCompatibility, cancellationToken: cancellationToken);
//
//         var command = new ChangeSchemaCompatibilityModeRequest {
//             SchemaName    = name,
//             Compatibility = originalCompatibility
//         };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityNotModified>();
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_schema_is_destroyed(CancellationToken cancellationToken) {
//         var (_, name) = await Register(cancellationToken: cancellationToken);
//
//         var destroyResult = await Apply(new DestroySchemaRequest { SchemaName = name }, cancellationToken);
//
//         destroyResult.Success.Should().BeTrue();
//
//         var command = new ChangeSchemaCompatibilityModeRequest {
//             SchemaName    = name,
//             Compatibility = Faker.Random.Enum(CompatibilityMode.Undefined)
//         };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityNotFound>();
//     }
// }