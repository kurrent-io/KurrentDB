using EventStore.Connectors.Management.Contracts.Queries;
using FluentValidation;

namespace EventStore.Connectors.Management;

[UsedImplicitly]
public class ListConnectorsValidator : AbstractValidator<ListConnectors> {
    public ListConnectorsValidator() {
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
}

[UsedImplicitly]
public class GetConnectorSettingsValidator() : RequestValidator<GetConnectorSettings>(x => x.ConnectorId);