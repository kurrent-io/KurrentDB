// using Google.Protobuf;
// using KurrentDB.SchemaRegistry.Tests.Fixtures;
// using KurrentDB.Protocol.Registry.V2;
// using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
// using KurrentDB.SchemaRegistry.Services.Domain;
// using Serilog;
//
// namespace KurrentDB.SchemaRegistry.Tests.Commands.Domain;
//
// public class DeleteSchemaVersionCommandTests : SchemaApplicationTestFixture {
//     [Test, Timeout(20_000)]
//     public async Task deletes_schema_version_when_more_than_one_version_exists(CancellationToken cancellationToken) {
//         var (_, name) = await Register(cancellationToken: cancellationToken);
//         var (id, _)   = await Register(schemaName: name, definition: ByteString.CopyFromUtf8(Faker.Lorem.Text()), cancellationToken: cancellationToken);
//
//         var command = new DeleteSchemaVersionRequest { SchemaVersionId = id };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeTrue();
//
//         var ok = result.Get()!;
//         ok.Changes.Should().HaveCount(1);
//
//         var evt = ok.Changes.First().Event as SchemaVersionDeleted;
//         evt.Should().NotBeNull();
//
//         evt.SchemaVersionId.Should().Be(id);
//         evt.SchemaName.Should().Be(name);
//         evt.DeletedAt.Should().Be(TimeProvider.GetUtcNow());
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task does_not_allow_delete_of_only_non_deleted_schema_version(CancellationToken cancellationToken) {
//         var (v1Id, name) = await Register(cancellationToken: cancellationToken);
//         var (v2Id, _)    = await Register(schemaName: name, definition: ByteString.CopyFromUtf8(Faker.Lorem.Text()), cancellationToken: cancellationToken);
//
//         var v1Result = await Apply(new DeleteSchemaVersionRequest { SchemaVersionId = v1Id }, cancellationToken);
//
//         v1Result.Success.Should().BeTrue();
//
//         var v2Result = await Apply(new DeleteSchemaVersionRequest { SchemaVersionId = v2Id }, cancellationToken);
//
//         v2Result.Success.Should().BeFalse();
//         v2Result.Exception.Should().BeOfType<DomainExceptions.EntityException>();
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_schema_version_does_not_exist(CancellationToken cancellationToken) {
//         var (_, name) = await Register(cancellationToken: cancellationToken);
//         await Register(schemaName: name, definition: ByteString.CopyFromUtf8(Faker.Lorem.Text()), cancellationToken: cancellationToken);
//
//         var command = new DeleteSchemaVersionRequest { SchemaVersionId = Guid.NewGuid().ToString() };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityNotFound>();
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_schema_version_is_already_deleted(CancellationToken cancellationToken) {
//         var (_, name) = await Register(cancellationToken: cancellationToken);
//         var (id, _)   = await Register(schemaName: name, definition: ByteString.CopyFromUtf8(Faker.Lorem.Text()), cancellationToken: cancellationToken);
//
//         var command = new DeleteSchemaVersionRequest { SchemaVersionId = id };
//
//         var originalDelete = await Apply(command, cancellationToken);
//
//         originalDelete.Success.Should().BeTrue();
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityDeleted>();
//     }
//
//     [Test, Timeout(20_000)]
//     public async Task throws_exception_when_schema_is_already_destroyed(CancellationToken cancellationToken) {
//         var (id, name) = await Register();
//
//         var destroyCommand = new DestroySchemaRequest { SchemaName = name };
//
//         var destroyResult = await Apply(destroyCommand, cancellationToken);
//
//         destroyResult.Success.Should().BeTrue();
//
//         var command = new DeleteSchemaVersionRequest { SchemaVersionId = id };
//
//         var result = await Apply(command, cancellationToken);
//         result.Success.Should().BeFalse();
//         result.Exception.Should().BeOfType<DomainExceptions.EntityNotFound>();
//     }
// }