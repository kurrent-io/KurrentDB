// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ConvertIfStatementToSwitchStatement

using FluentValidation;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Streams;
using static KurrentDB.Protocol.V2.Streams.ConsistencyCheck;

namespace KurrentDB.Api.Streams.Validators;

class AppendRecordsRequestValidator : RequestValidator<AppendRecordsRequest> {
	public AppendRecordsRequestValidator() {
		RuleFor(x => x.Records)
			.NotEmpty()
			.WithMessage("Append records request must contain at least one record.");

		RuleForEach(x => x.Checks)
			.SetValidator(ConsistencyCheckValidator.Instance);

		RuleFor(x => x.Checks)
			.Must(checks => {
				var streams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				return checks
					.Where(c => c.TypeCase is TypeOneofCase.StreamState)
					.All(c => streams.Add(c.StreamState.Stream));
			})
			.WithMessage("Each stream can only appear once in consistency checks.");

        // ------------------------------------------------------------------------------
        // Uncomment when we add support for query predicate checks
        // ------------------------------------------------------------------------------
        // Limits to one query predicate per request to avoid ambiguous conditional logic
        // and maintain simple transaction semantics.
        // ------------------------------------------------------------------------------
		// RuleFor(x => x.Checks)
		// 	.Must(checks => checks
		// 		.Count(c => c.TypeCase is TypeOneofCase.QueryPredicate) <= 1)
		// 	.WithMessage("Only one query predicate check is allowed.");
	}
}
