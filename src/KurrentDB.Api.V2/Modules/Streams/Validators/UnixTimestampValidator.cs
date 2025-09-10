// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using FluentValidation.Results;
using KurrentDB.Api.Infrastructure.Validation;

namespace KurrentDB.Api.Streams.Validators;

class UnixTimestampValidator : EnhancedValidator<long> {
	public static readonly UnixTimestampValidator Instance = new(UnixTimestampValidatorOptions.Default);

	public UnixTimestampValidator(UnixTimestampValidatorOptions options) {
		RuleFor(x => x)
			.Custom((value, ctx) => {
				try {
					DateTimeOffset.FromUnixTimeMilliseconds(value);
				}
				catch {
					ctx.AddFailure("Timestamp must be a positive value representing milliseconds since Unix epoch");
				}

				if (options.HasNoAllowance) return;

				var now = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();

				if (options.HasFutureAllowance && value > now + options.FutureAllowanceMilliseconds)
					ctx.AddFailure(new ValidationFailure("", $"Timestamp must not be in the future (beyond {options.FutureAllowanceMilliseconds}ms clock skew allowance)") {
						ErrorCode = "FUTURE_TIMESTAMP",
						Severity  = Severity.Warning
					});

				if (options.HasPastAllowance && value < now - options.PastAllowanceMilliseconds)
					ctx.AddFailure($"Timestamp must not be in the past (beyond {options.PastAllowanceMilliseconds}ms clock skew allowance)");
			});
	}

	public UnixTimestampValidator() : this(UnixTimestampValidatorOptions.Default) { }
}

/// <summary>
/// Options for configuring the behavior of <see cref="UnixTimestampValidator"/>
/// </summary>
/// <param name="PastAllowanceMilliseconds">
/// The number of milliseconds in the past to allow timestamps to be.
/// A value of -1 indicates no allowance (default).
/// </param>
/// <param name="FutureAllowanceMilliseconds">
/// The number of milliseconds in the future to allow timestamps to be.
/// A value of -1 indicates no allowance (default).
/// </param>
readonly record struct UnixTimestampValidatorOptions(int PastAllowanceMilliseconds = -1, int FutureAllowanceMilliseconds = -1) {
	public static readonly UnixTimestampValidatorOptions Default = new();

	public bool HasPastAllowance   => PastAllowanceMilliseconds > -1;
	public bool HasFutureAllowance => FutureAllowanceMilliseconds > -1;
	public bool HasNoAllowance     => !HasPastAllowance && !HasFutureAllowance;

}
