using Eventuous;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.SchemaRegistry.Domain;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SchemaRegistry.Tests.Fixtures;

public abstract class SchemaApplicationTestFixture : SchemaRegistryServerTestFixture {
    protected async ValueTask<Result<SchemaEntity>.Ok> Apply<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : class {
        var eventStore = TestServer.Services.GetRequiredService<SurgeEventStore>();
        var lookup     = TestServer.Services.GetRequiredService<LookupSchemaNameByVersionId>();

        var application = new SchemaApplication(new NJsonSchemaCompatibilityManager(), lookup, TimeProvider.GetUtcNow, eventStore);

        var result = await application.Handle(command, cancellationToken);

        result.ThrowIfError();

        return result.Get()!;
    }
}