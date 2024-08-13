using EventStore.Connectors.Management.Contracts.Queries;
using FluentValidation;

namespace EventStore.Connectors.Management.Queries;

[PublicAPI]
class ListConnectorsQueryValidator : AbstractValidator<ListConnectors> {
    static readonly ListConnectorsQueryValidator Instance = new();

    ListConnectorsQueryValidator() {
        RuleFor(x => x.Paging.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.")
            .When(x => x.Paging is not null);

        RuleFor(x => x.Paging.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page size must be greater than or equal to 1.")
            .LessThanOrEqualTo(100)
            .WithMessage("Page size must be less than or equal to 100.")
            .When(x => x.Paging is not null);
    }

    public static void EnsureValid(ListConnectors query) => Instance.ValidateAndThrow(query);
}

[PublicAPI]
class GetConnectorSettingsQueryValidator : AbstractValidator<GetConnectorSettings> {
    static readonly GetConnectorSettingsQueryValidator Instance = new();

    GetConnectorSettingsQueryValidator() {
        RuleFor(x => x.ConnectorId)
            .NotEmpty();
    }

    public static void EnsureValid(GetConnectorSettings query) => Instance.ValidateAndThrow(query);
}