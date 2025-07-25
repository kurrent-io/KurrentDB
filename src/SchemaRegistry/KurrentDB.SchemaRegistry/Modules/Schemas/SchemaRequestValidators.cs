// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using Google.Protobuf;
using KurrentDB.Protocol.Registry.V2;

namespace KurrentDB.SchemaRegistry;

[UsedImplicitly]
public class CreateSchemaRequestValidator : AbstractValidator<CreateSchemaRequest> {
    public static readonly CreateSchemaRequestValidator Instance = new();

    public CreateSchemaRequestValidator() {
        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance);

        RuleFor(x => x.Details.DataFormat)
            .SetValidator(SchemaDataFormatValidator.Instance);

        RuleFor(x => x.Details.Compatibility)
            .SetValidator(CompatibilityModeValidator.Instance);

        RuleFor(x => x.SchemaDefinition)
            .SetValidator(SchemaDefinitionValidator.Instance);
    }
}

[UsedImplicitly]
public class RegisterSchemaVersionRequestValidator : AbstractValidator<RegisterSchemaVersionRequest> {
    public static readonly RegisterSchemaVersionRequestValidator Instance = new();

    public RegisterSchemaVersionRequestValidator() {
        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance);

        RuleFor(x => x.SchemaDefinition)
            .SetValidator(SchemaDefinitionValidator.Instance);
    }
}

[UsedImplicitly]
public class UpdateSchemaRequestValidator : AbstractValidator<UpdateSchemaRequest> {
    public static readonly UpdateSchemaRequestValidator Instance = new();

    public UpdateSchemaRequestValidator() {
        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance);

        RuleFor(x => x.Details.Compatibility)
            .SetValidator(CompatibilityModeValidator.Instance);

        RuleFor(x => x.Details.DataFormat)
            .SetValidator(SchemaDataFormatValidator.Instance);
    }
}

[UsedImplicitly]
public class DeleteSchemaVersionsRequestValidator : AbstractValidator<DeleteSchemaVersionsRequest> {
    public static readonly DeleteSchemaVersionsRequestValidator Instance = new();

    public DeleteSchemaVersionsRequestValidator() {
        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance);

        RuleFor(x => x.Versions)
            .NotEmpty();
    }
}

[UsedImplicitly]
public class DeleteSchemaRequestValidator : AbstractValidator<DeleteSchemaRequest> {
    public static readonly DeleteSchemaRequestValidator Instance = new();

    public DeleteSchemaRequestValidator() =>
        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance);
}

[UsedImplicitly]
public class CheckSchemaCompatibilityRequestValidator : AbstractValidator<CheckSchemaCompatibilityRequest> {
    public static readonly CheckSchemaCompatibilityRequestValidator Instance = new();

    public CheckSchemaCompatibilityRequestValidator() {
        RuleFor(x => x.SchemaVersionId)
            .SetValidator(SchemaVersionIdValidator.Instance)
            .Unless(x => x.HasSchemaName);

        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance)
            .Unless(x => x.HasSchemaVersionId);

        RuleFor(x => x.Definition)
            .SetValidator(SchemaDefinitionValidator.Instance);

        // This should be a domain level exception
        // RuleFor(x => x.DataFormat)
        //     .SetValidator(SchemaDataFormatValidator.Instance)
        //     .Must((_, format) => format == SchemaDataFormat.Json)
        //     .WithMessage("Schema format must be JSON for compatibility check");
    }
}

[UsedImplicitly]
public class GetSchemaRequestValidator : AbstractValidator<GetSchemaRequest> {
    public static readonly GetSchemaRequestValidator Instance = new();

    public GetSchemaRequestValidator() {
        RuleFor(x => x.SchemaName)
            .NotEmpty()
            .WithMessage("Schema name must not be empty");
    }
}

[UsedImplicitly]
public class GetSchemaVersionRequestValidator : AbstractValidator<GetSchemaVersionRequest> {
    public static readonly GetSchemaVersionRequestValidator Instance = new();

    public GetSchemaVersionRequestValidator() {
        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance);

        RuleFor(x => x.VersionNumber)
	        .GreaterThanOrEqualTo(1)
	        .WithMessage("Version number must be greater than or equal to 1")
	        .When(x => x.HasVersionNumber);
    }
}

[UsedImplicitly]
public class GetSchemaVersionByIdRequestValidator : AbstractValidator<GetSchemaVersionByIdRequest> {
    public static readonly GetSchemaVersionByIdRequestValidator Instance = new();

    public GetSchemaVersionByIdRequestValidator() =>
        RuleFor(x => x.SchemaVersionId)
            .SetValidator(SchemaVersionIdValidator.Instance);
}

[UsedImplicitly]
public class ListRegisteredSchemasRequestValidator : AbstractValidator<ListRegisteredSchemasRequest> {
    public static readonly ListRegisteredSchemasRequestValidator Instance = new();

    public ListRegisteredSchemasRequestValidator() {
        // RuleFor(x => x.NamePrefix)
        //     .NotEmpty()
        //     .WithMessage("Schema name must not be empty");
    }
}

[UsedImplicitly]
public class ListSchemasRequestValidator : AbstractValidator<ListSchemasRequest> {
    public static readonly ListSchemaVersionsRequestValidator Instance = new();

    public ListSchemasRequestValidator() {
        // RuleFor(x => x.NamePrefix)
        //     .NotEmpty()
        //     .WithMessage("Schema name must not be empty");
    }
}

[UsedImplicitly]
public class ListSchemaVersionsRequestValidator : AbstractValidator<ListSchemaVersionsRequest> {
    public static readonly ListSchemaVersionsRequestValidator Instance = new();

    public ListSchemaVersionsRequestValidator() =>
        RuleFor(x => x.SchemaName)
            .SetValidator(SchemaNameValidator.Instance);
}

[UsedImplicitly]
public class LookupSchemaNameRequestValidator : AbstractValidator<LookupSchemaNameRequest> {
    public static readonly LookupSchemaNameRequestValidator Instance = new();

    public LookupSchemaNameRequestValidator() =>
        RuleFor(x => x.SchemaVersionId)
            .SetValidator(SchemaVersionIdValidator.Instance);
}

[UsedImplicitly]
public partial class SchemaNameValidator : AbstractValidator<string> {
    public static readonly SchemaNameValidator Instance = new();

    public SchemaNameValidator() =>
        RuleFor(x => x)
            .NotEmpty()
            .Matches(RegEx())
            .WithMessage("Schema name must not be empty and can only contain alphanumeric characters, underscores, dashes, and periods");

    [System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z0-9_.-]+$")]
    private static partial System.Text.RegularExpressions.Regex RegEx();
}

[UsedImplicitly]
public class SchemaVersionIdValidator : AbstractValidator<string> {
    public static readonly SchemaVersionIdValidator Instance = new();

    public SchemaVersionIdValidator() {
        RuleFor(x => x)
            .Must(value => Guid.TryParse(value, out var valueGuid) && valueGuid != Guid.Empty)
            .WithMessage("Schema version ID must be a valid UUID");
    }
}

[UsedImplicitly]
public class SchemaDefinitionValidator : AbstractValidator<ByteString> {
    public static readonly SchemaDefinitionValidator Instance = new();

    public SchemaDefinitionValidator() =>
        RuleFor(x => x)
            .Must((_, definition) => !definition.IsEmpty)
            .WithMessage("Schema definition must not be empty");
}

[UsedImplicitly]
public class CompatibilityModeValidator : AbstractValidator<CompatibilityMode> {
    public static readonly CompatibilityModeValidator Instance = new();

    public CompatibilityModeValidator() =>
        RuleFor(x => x)
            .IsInEnum()
            .WithMessage("Compatibility mode must be a valid enum value")
            .NotEqual(CompatibilityMode.Unspecified)
            .WithMessage("Compatibility mode must not be unspecified");
}

[UsedImplicitly]
public class SchemaDataFormatValidator : AbstractValidator<SchemaDataFormat> {
    public static readonly SchemaDataFormatValidator Instance = new();

    public SchemaDataFormatValidator() =>
        RuleFor(x => x)
            .IsInEnum()
            .WithMessage("Schema data format must be a valid enum value")
            .NotEqual(SchemaDataFormat.Unspecified)
            .WithMessage("Schema data format must not be unspecified");
}
