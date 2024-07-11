using EventStore.Streaming.Schema;

public static class MessageRegistration {
    // public static async ValueTask RegisterSystemMessages(this SchemaRegistry registry, SchemaDefinitionType schemaType, CancellationToken cancellationToken) {
    //     // processors
    //     await registry.RegisterSystemMessage<ProcessorStateChanged>(schemaType, cancellationToken);
    //     await registry.RegisterSystemMessage<ProcessorLeaseRenewed>(schemaType, cancellationToken);
    //
    //     // consumers
    //     await registry.RegisterSystemMessage<Checkpoint>(schemaType, cancellationToken);
    //
    //     // leases
    //     await registry.RegisterSystemMessage<Lease>(schemaType, cancellationToken);
    // }

    public static ValueTask<RegisteredSchema> RegisterSystemMessage(
        this SchemaRegistry registry, Type messageType, SchemaDefinitionType schemaType, CancellationToken cancellationToken = default
    ) {
        var subject    = SchemaSubjectNameStrategies.ForSystemMessage(messageType).GetSubjectName();
        var schemaInfo = new SchemaInfo(subject, schemaType);
        return registry.RegisterSchema(schemaInfo, messageType, cancellationToken);
    }

    public static ValueTask<RegisteredSchema> RegisterSystemMessage<T>(
        this SchemaRegistry registry, SchemaDefinitionType schemaType = SchemaDefinitionType.Json, CancellationToken cancellationToken = default
    ) => registry.RegisterSystemMessage(typeof(T), schemaType, cancellationToken);
}