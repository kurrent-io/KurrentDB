// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using FluentValidation;
using Humanizer;
using KurrentDB.Api.Infrastructure.Grpc.Validation;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams.Validators;

class AppendRecordValidator : RequestValidator<AppendRecord> {
	public static readonly AppendRecordValidator Instance = new(AppendRecordValidatorOptions.Default);

	public AppendRecordValidator(AppendRecordValidatorOptions options) {
		RuleFor(x => x.RecordId)
			.SetValidator(RecordIdValidator.Instance)
			.When(x => x.HasRecordId);

		// RuleFor(x => x.Timestamp)
		// 	.SetValidator(UnixTimestampValidator.Instance)
		// 	.When(x => x.HasTimestamp);

		RuleFor(x => x.Schema.Format)
			.SetValidator(SchemaFormatValidator.Instance);

		RuleFor(x => x.Schema.Name)
			.SetValidator(SchemaNameValidator.Instance);

		RuleFor(x => x.Schema.Id)
			.SetValidator(SchemaIdValidator.Instance)
			.When(x => x.Schema.HasId);

		RuleFor(x => x)
			.Custom((record, ctx) => {
				Debug.Assert(record.HasRecordId, "RecordId should have been set by the time we get here");
				//Debug.Assert(record.HasTimestamp, "Timestamp should have been set by the time we get here");

                var recordSize = record.CalculateSizeOnDisk(options.MaxRecordSize);

                if (!recordSize.ExceedsMax)
                    return;

				var message = $"Record size exceeds the maximum allowed size of {options.MaxRecordSize.Bytes().Humanize("0.00")} bytes by" +
				              $" {recordSize.SizeExceededBy.Bytes().Humanize("0.00")} with a total size of {recordSize.TotalSize.Bytes().Humanize("0.00")}";

				ctx.AddFailure(message);
			});
	}
}

public record AppendRecordValidatorOptions(int MaxRecordSize = TFConsts.EffectiveMaxLogRecordSize) {
    public static readonly AppendRecordValidatorOptions Default = new();
}
