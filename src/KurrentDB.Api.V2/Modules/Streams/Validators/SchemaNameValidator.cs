// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;

namespace KurrentDB.Api.Streams.Validators;

partial class SchemaNameValidator : ValidatorBase<SchemaNameValidator, string?> {
	public SchemaNameValidator() =>
		RuleFor(x => x)
			.NotEmpty()
			.WithMessage("Schema name must not be empty")
			.Must(s => string.IsNullOrEmpty(s) || s.IsNormalized(NormalizationForm.FormC))
			.WithMessage("Schema name must be in Unicode NFC (Normalization Form C)")
			.Matches(RegEx())
			.WithMessage("Schema name can only contain unicode letters, digits, underscores, dashes, periods, colons, and dollar signs. Attempted Value: {PropertyValue}");

	[System.Text.RegularExpressions.GeneratedRegex(@"^[\p{L}\p{N}_.$:-]+$")]
	private static partial System.Text.RegularExpressions.Regex RegEx();
}
