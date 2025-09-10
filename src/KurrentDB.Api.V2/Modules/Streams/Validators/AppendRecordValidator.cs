// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using FluentValidation;
using KurrentDB.Api.Infrastructure.Validation;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

readonly record struct AppendRecordValidatorOptions(int MaxRecordSize = TFConsts.EffectiveMaxLogRecordSize) {
	public static readonly AppendRecordValidatorOptions Default = new();
}

class AppendRecordValidator : EnhancedValidator<AppendRecord> {
	public static readonly AppendRecordValidator Instance = new();

	public AppendRecordValidator(AppendRecordValidatorOptions? options = null) {
		options ??= AppendRecordValidatorOptions.Default;

		RuleFor(x => x.RecordId)
			.SetValidator(RecordIdValidator.Instance)
			.When(x => x.HasRecordId);

		RuleFor(x => x.Timestamp)
			.SetValidator(UnixTimestampValidator.Instance)
			.When(x => x.HasTimestamp);

		RuleFor(x => x.Schema.Format)
			.SetValidator(SchemaFormatValidator.Instance);

		RuleFor(x => x.Schema.Name)
			.SetValidator(SchemaNameValidator.Instance);

		RuleFor(x => x.Schema.Id)
			.SetValidator(SchemaIdValidator.Instance)
			.When(x => x.Schema.HasId);

		// RuleFor(x => x)
		// 	.Custom((record, ctx) => {
		// 		Debug.Assert(record.HasRecordId, "RecordId should have been set by the time we get here");
		// 		Debug.Assert(record.HasTimestamp, "Timestamp should have been set by the time we get here");
		//
		// 		var recordSize = record.CalculateSize();
		//
		// 		if (recordSize < options.MaxRecordSize) return;
		//
		// 		var exceededBy = recordSize - options.MaxRecordSize;
		//
		// 		var message = $"Record size exceeds the maximum allowed size of {options.MaxRecordSize.Bytes().Humanize("0.00")} bytes by" +
		// 		              $" {exceededBy.Bytes().Humanize("0.00")} with a total size of {recordSize.Bytes().Humanize("0.00")}";
		//
		// 		var failure = new ValidationFailure(nameof(AppendRecord), message) { ErrorCode = "MAX_RECORD_SIZE_EXCEEDED" };
		//
		// 		ctx.AddFailure(failure);
		// 	});
	}
}
