// using KurrentDB.SchemaRegistry.Tests.Fixtures;
// using KurrentDB.Protocol.Registry.V2;
// using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
// using KurrentDB.SchemaRegistry.Services.Domain;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Domain;
//
// public class DestroySchemaCommandTests : SchemaApplicationTestFixture {
//     [Test, Timeout(20_000)]
//     public async Task destroy_schema(CancellationToken cancellationToken) {
//         var (_, name) = await Register(cancellationToken: cancellationToken);
//
//         var command = new DestroySchemaRequest {
//             SchemaName = name
//         };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeTrue();
//
//         var ok = result.Get()!;
//         ok.Changes.Should().HaveCount(1);
//
//         var evt = ok.Changes.First().Event as SchemaDestroyed;
//         evt.Should().NotBeNull();
//
//         evt.SchemaName.Should().Be(name);
//         evt.DestroyedAt.Should().Be(TimeProvider.GetUtcNow());
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_schema_is_already_destroyed(CancellationToken cancellationToken) {
//         var (_, name) = await Register(cancellationToken: cancellationToken);
//
//         var command = new DestroySchemaRequest {
//             SchemaName = name
//         };
//
//         var originalDestroy = await Apply(command, cancellationToken);
//         originalDestroy.Success.Should().BeTrue();
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityNotFound>();
//     }
// }