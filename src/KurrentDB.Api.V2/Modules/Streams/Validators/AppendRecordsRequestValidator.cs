// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

class AppendRecordsRequestValidator : RequestValidator<AppendRecordsRequest> {
	public AppendRecordsRequestValidator() {
		RuleFor(x => x.Records)
			.NotEmpty()
			.WithMessage("Append records request must contain at least one record.");

		RuleForEach(x => x.Records)
			.ChildRules(record => {
				record.RuleFor(r => r.Stream)
					.SetValidator(StreamNameValidator.Instance);
			});

		RuleForEach(x => x.ConsistencyChecks)
			.SetValidator(ConsistencyCheckValidator.Instance);

		RuleFor(x => x.ConsistencyChecks)
			.Must(checks => {
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

				return checks
					.Where(check => check.KindCase == ConsistencyCheck.KindOneofCase.Revision)
					.All(check => seen.Add(check.Revision.Stream));
			})
			.WithMessage("Each stream can only appear once in consistency checks.");
	}
}
