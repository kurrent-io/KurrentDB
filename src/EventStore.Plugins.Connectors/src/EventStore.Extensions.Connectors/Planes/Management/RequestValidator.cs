using System.Linq.Expressions;
using FluentValidation;
using Google.Protobuf;

namespace EventStore.Connectors.Management;

public abstract class RequestValidator<T> : AbstractValidator<T> where T : class, IMessage {
    protected RequestValidator(Expression<Func<T, string>> getConnectId) {
        RuleFor(getConnectId)
            .NotEmpty().WithMessage("ConnectorId must not be empty")
            .Length(5, 50).WithMessage("ConnectorId must be between 5 and 50 characters long")
            .Matches("^[a-zA-Z0-9_-]+$")
            .WithMessage("ConnectorId can only contain alphanumeric characters, hyphens, and underscores");
    }

    protected void AddNameValidationRules(
        Expression<Func<T, string?>> selector
    ) =>
        RuleFor(selector)
            .Length(3, 50)
            .WithMessage("name must be between 3 and 50 characters long");
}