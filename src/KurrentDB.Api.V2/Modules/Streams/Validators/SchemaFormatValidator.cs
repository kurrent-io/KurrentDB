// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Validation;

namespace KurrentDB.Api.Streams.Validators;

class SchemaFormatValidator : EnhancedValidator<Protocol.V2.Streams.SchemaFormat> {
	public static readonly SchemaFormatValidator Instance = new();

	public SchemaFormatValidator() =>
		RuleFor(x => x)
			.IsInEnum()
			.WithMessage("Schema format must be one of Json, Protobuf or Bytes")
			.NotEqual(Protocol.V2.Streams.SchemaFormat.Unspecified)
			.WithMessage("Schema format must not be unspecified");
}
