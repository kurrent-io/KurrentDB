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
					.NotEmpty()
					.WithMessage("Each record must specify a target stream.")
					.Must(s => !s.StartsWith('$'))
					.WithMessage("Records cannot target system streams (starting with '$').")
					.When(r => r.HasStream);

				record.RuleFor(r => r.HasStream)
					.Equal(true)
					.WithMessage("Each record must specify a target stream.");
			});

		RuleForEach(x => x.ConsistencyChecks)
			.SetValidator(ConsistencyCheckValidator.Instance);

		RuleFor(x => x.ConsistencyChecks)
			.Must(checks => {
				var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				return checks
					.Where(c => c.KindCase == ConsistencyCheck.KindOneofCase.Revision)
					.All(c => seen.Add(c.Revision.Stream));
			})
			.WithMessage("Each stream can only appear once in consistency checks. Use a single check per stream.")
			.When(x => x.ConsistencyChecks.Count > 1);
	}
}
