// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

class AppendRecordValidator : RequestValidator<AppendRecord> {
    public static readonly AppendRecordValidator Instance = new();

    public AppendRecordValidator() {
	    RuleFor(x => x.Stream)
		    .SetValidator(StreamNameValidator.Instance)
		    .When(x => x.HasStream);

        RuleFor(x => x.RecordId)
            .SetValidator(RecordIdValidator.Instance)
            .When(x => x.HasRecordId);

        RuleFor(x => x.Schema.Format)
            .SetValidator(SchemaFormatValidator.Instance);

        RuleFor(x => x.Schema.Name)
            .SetValidator(SchemaNameValidator.Instance);

        RuleFor(x => x.Schema.Id)
            .SetValidator(SchemaIdValidator.Instance)
            .When(x => x.Schema.HasId);

        RuleForEach(x => x.Properties)
            .ChildRules(v => {
                v.RuleFor(x => x.Key)
                    .NotEmpty()
                    .WithMessage("Property keys must not be empty.");
            });

        // ------------------------------------------------------------------------------
        // Uncomment for debugging purposes
        // ------------------------------------------------------------------------------
        // This will validate that the properties can be serialized to a Protobuf Struct
        // which is what we use to store them internally.
        // And also validates that the struct can be serialized to json.
        // ------------------------------------------------------------------------------
        // RuleFor(x => x.Properties)
        //     .Custom((entry, ctx) => {
        //         try {
        //             _ = JsonFormatter.Default.Format(new Struct { Fields = { entry } });
        //         }
        //         catch (Exception ex) {
        //             ctx.AddFailure(ex.Message);
        //         }
        //     });

    }
}
