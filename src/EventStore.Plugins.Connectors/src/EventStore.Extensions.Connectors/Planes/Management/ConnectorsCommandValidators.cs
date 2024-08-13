using EventStore.Connectors.Management.Contracts.Commands;
using FluentValidation;

namespace EventStore.Connectors.Management;

class CreateConnectorValidator : AbstractValidator<CreateConnector> {
    static readonly CreateConnectorValidator Instance = new();

    CreateConnectorValidator() {
        RuleFor(x => x.ConnectorId).NotEmpty().WithMessage("ConnectorId is required");
    }

    public static void EnsureValid(CreateConnector cmd) => Instance.ValidateAndThrow(cmd);
}