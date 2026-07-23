// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using FluentValidation;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Indexes;

namespace KurrentDB.Api.Modules.Indexes.Validators;

class CreateIndexValidator : RequestValidator<CreateIndexRequest> {
	public static readonly CreateIndexValidator Instance = new();

	private const int MaxFields = 64;

	private CreateIndexValidator() {
		RuleFor(x => x.Name)
			.SetValidator(IndexNameValidator.Instance);

		RuleFor(x => x.Filter)
			.SetValidator(FilterValidator.Instance);

		RuleForEach(x => x.Fields)
			.SetValidator(FieldValidator.Instance);

		RuleFor(x => x.Fields)
			.Must(fields => fields.Count <= MaxFields)
			.WithMessage($"An index can have at most {MaxFields} fields");

		RuleFor(x => x.Fields)
			.Must(fields => fields.Select(f => f.Name).Distinct().Count() == fields.Count)
			.WithMessage("Index field names must be unique within a request");

		RuleFor(x => x)
			.Must(x =>
				x.Fields.Count > 0 ||
				!string.IsNullOrWhiteSpace(x.Filter))
			.WithMessage("At least a filter or a field must be provided");
	}
}
