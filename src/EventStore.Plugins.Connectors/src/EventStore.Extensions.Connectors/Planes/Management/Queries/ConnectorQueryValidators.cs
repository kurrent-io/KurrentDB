using EventStore.Connectors.Management.Contracts.Queries;
using FluentValidation;
using FluentValidation.Results;

namespace EventStore.Connectors.Management.Queries;

public static class ConnectorQueryValidators {
    public static void Validate(ListConnectorsQuery query) => Validate(query, new ListConnectorsQueryValidator());

    static void Validate<TQuery>(TQuery query, AbstractValidator<TQuery> validator) {
        var validationResult = validator.Validate(query);

        if (!validationResult.IsValid)
            throw new InvalidConnectorQueryException(validationResult.Errors);
    }

    class ListConnectorsQueryValidator : AbstractValidator<ListConnectorsQuery> {
        public ListConnectorsQueryValidator() {
            RuleFor(x => x.Paging)
                .NotNull()
                .WithMessage("Paging is required.");

            RuleFor(x => x.Paging.Page)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Page must be greater than or equal to 1.");

            RuleFor(x => x.Paging.PageSize)
                .GreaterThanOrEqualTo(1)
                .WithMessage("PageSize must be greater than or equal to 1.")
                .LessThanOrEqualTo(100)
                .WithMessage("PageSize must be less than or equal to 100.");
        }
    }
}

public class InvalidConnectorQueryException(Dictionary<string, string[]> errors)
    : Exception("Invalid connector query") {
    public Dictionary<string, string[]> Errors { get; } = errors;

    public InvalidConnectorQueryException(List<ValidationFailure> failures)
        : this(failures.GroupBy(x => x.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())) { }
}