// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

class ConsistencyCheckValidator : ValidatorBase<ConsistencyCheckValidator, ConsistencyCheck> {
	static readonly List<long> ValidExpectedRevisions = [
		(long)ExpectedRevisionConstants.NoStream,
		(long)ExpectedRevisionConstants.Exists
	];

	public ConsistencyCheckValidator() {
		RuleFor(x => x.KindCase)
			.NotEqual(ConsistencyCheck.KindOneofCase.None)
			.WithMessage("Each consistency check must specify a kind.");

		When(x => x.KindCase == ConsistencyCheck.KindOneofCase.Revision, () => {
			RuleFor(x => x.Revision.Stream)
				.SetValidator(StreamNameValidator.Instance);

			RuleFor(x => x.Revision.Revision)
				.Must(x => x >= 0 || ValidExpectedRevisions.Contains(x))
				.WithMessage("Expected revision must be a specific revision (>= 0), NoStream (-1), or Exists (-4); Any (-2) is not allowed.");
		});
	}
}
