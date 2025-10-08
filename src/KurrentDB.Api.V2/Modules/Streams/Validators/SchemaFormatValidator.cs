using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

class SchemaFormatValidator : ValidatorBase<SchemaFormatValidator, SchemaFormat> {
    public SchemaFormatValidator() =>
        RuleFor(x => x)
            .IsInEnum()
            .WithMessage("{PropertyName} must be one of Json, Protobuf or Bytes.")
            .NotEqual(SchemaFormat.Unspecified)
            .WithMessage("{PropertyName} must not be Unspecified.")
            .WithName("Schema format");
}
