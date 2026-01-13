// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.Api.Modules.Indexes.Validators;

class CreateIndexValidator : RequestValidator<CreateIndexRequest> {
	public static readonly CreateIndexValidator Instance = new();

	private CreateIndexValidator() {
		RuleFor(x => x.Name)
			.SetValidator(IndexNameValidator.Instance);

		RuleFor(x => x.Filter)
			.SetValidator(FilterValidator.Instance);

		RuleForEach(x => x.Fields)
			.SetValidator(FieldValidator.Instance);

		// todo: when we allow multiple fields, their names will need to be unique within the request
		RuleFor(x => x.Fields)
			.Must(x => x.Count <= 1)
			.WithMessage("Currently at most one index field must be provided");

		RuleFor(x => x)
			.Must(x =>
				x.Fields.Count > 0 ||
				!string.IsNullOrWhiteSpace(x.Filter))
			.WithMessage("At least a filter or a field must be provided");
	}
}
