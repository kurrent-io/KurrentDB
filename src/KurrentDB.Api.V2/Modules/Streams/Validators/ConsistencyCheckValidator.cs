// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Core.Data;
using KurrentDB.Protocol.V2.Streams;
using static KurrentDB.Protocol.V2.Streams.ConsistencyCheck;

namespace KurrentDB.Api.Streams.Validators;

class ConsistencyCheckValidator : ValidatorBase<ConsistencyCheckValidator, ConsistencyCheck> {
	static readonly List<long> ValidExpectedRevisions = [
		ExpectedVersion.NoStream,
		ExpectedVersion.StreamExists
	];

	public ConsistencyCheckValidator() {
		RuleFor(x => x.TypeCase)
			.NotEqual(TypeOneofCase.None)
			.WithMessage("Each consistency check must specify a type.");

		When(x => x.TypeCase is TypeOneofCase.StreamState, () => {
			RuleFor(x => x.StreamState.Stream)
				.SetValidator(StreamNameValidator.Instance);

			RuleFor(x => x.StreamState.ExpectedState)
				.Must(x => x >= 0 || ValidExpectedRevisions.Contains(x))
				.WithMessage("Expected state must be positive or one of the allowed constants: NoStream (-1) or Exists (-4). Any (-2) is not allowed.");
		});
	}
}
