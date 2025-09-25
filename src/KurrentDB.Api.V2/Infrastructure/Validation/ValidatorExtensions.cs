using FluentValidation;
using Google.Protobuf;
using KurrentDB.Api.Errors;

namespace KurrentDB.Api.Infrastructure.Validation;

static class ValidatorExtensions {
    public static T EnsureValid<T>(this IValidator<T> validator, T request) where T : IMessage {
        var result = validator.Validate(request);
        return !result.IsValid ? throw ApiErrors.InvalidRequest(result) : request;
    }
}
