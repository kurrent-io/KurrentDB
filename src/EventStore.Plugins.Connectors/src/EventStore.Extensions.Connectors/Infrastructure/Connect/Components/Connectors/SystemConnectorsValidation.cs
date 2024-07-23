// ReSharper disable CheckNamespace

using System.Diagnostics.CodeAnalysis;
using EventStore.Connectors.Kafka;
using EventStore.Streaming;
using FluentValidation.Results;
using Microsoft.Extensions.Configuration;

namespace EventStore.Connect.Connectors;

public class SystemConnectorsValidation : ConnectorsValidationBase {
    Dictionary<string, IConnectorValidator> Validators { get; } = new() {
        // { typeof(HttpSink).FullName!, new HttpSinkValidator() },
        { typeof(KafkaSink).FullName!, new KafkaSinkValidator() }
    };

    protected override bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, out IConnectorValidator validator) =>
        Validators.TryGetValue(connectorTypeName, out validator!);
}

public abstract class ConnectorsValidationBase : IConnectorValidator {
    public ValidationResult ValidateConfiguration(IConfiguration configuration) {
        var connectorTypeName = configuration
            .GetRequiredOptions<ConnectorOptions>()
            .InstanceTypeName;

        if (string.IsNullOrWhiteSpace(connectorTypeName))
            return ConnectorInstanceTypeNameMissing();

        return TryGetConnectorValidator(connectorTypeName, out var validator)
            ? validator.ValidateConfiguration(configuration)
            : ValidatorNotFoundFailure(connectorTypeName);

        static ValidationResult ConnectorInstanceTypeNameMissing() =>
            new([new ValidationFailure("ConnectorInstanceTypeName", "Failed to extract connector instance type name from configuration")]);

        static ValidationResult ValidatorNotFoundFailure(string connectorTypeName) =>
            new([new ValidationFailure("ConnectorTypeName", $"Failed to find validator for {connectorTypeName}")]);
    }

    public ValidationResult ValidateConfiguration(IDictionary<string, string?> configuration) =>
        ValidateConfiguration(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

    public void EnsureValid(IDictionary<string, string?> configuration) =>
        EnsureValid(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

    public void EnsureValid(IConfiguration configuration) {
        var result = ValidateConfiguration(configuration);
        if (!result.IsValid)
            throw new FluentValidation.ValidationException(result.Errors);
    }

    protected abstract bool TryGetConnectorValidator(ConnectorInstanceTypeName connectorTypeName, [MaybeNullWhen(false)] out IConnectorValidator validator);
}