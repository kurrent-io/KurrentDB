// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable MethodHasAsyncOverload

using FluentValidation;
using Google.Protobuf;
using KurrentDB.Api.Infrastructure.FluentValidation;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Api.Tests.Infrastructure;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Streams.Validators;

[Category("Validation")]
public class AppendRecordsRequestValidatorTests {
	static readonly AppendRecordsRequestValidator Validator = new();

	[Test]
	public async ValueTask minimal_request_passes() {
		var request = CreateValidRequest();
		var result = Validator.Validate(request);

		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async ValueTask empty_records_fails() {
		var request = new AppendRecordsRequest();

		var vex = await Assert
			.That(() => Validator.ValidateAndThrow(request))
			.Throws<DetailedValidationException>();

		vex.LogValidationErrors<AppendRecordsRequestValidator>();
	}

	[Test]
	public async ValueTask record_missing_stream_fails() {
		var request = new AppendRecordsRequest {
			Records = { CreateRecord() }
		};

		var vex = await Assert
			.That(() => Validator.ValidateAndThrow(request))
			.Throws<DetailedValidationException>();

		vex.LogValidationErrors<AppendRecordsRequestValidator>();
	}

	[Test]
	public async ValueTask check_missing_kind_fails() {
		var request = CreateValidRequest();
		request.ConsistencyChecks.Add(new ConsistencyCheck());

		var vex = await Assert
			.That(() => Validator.ValidateAndThrow(request))
			.Throws<DetailedValidationException>();

		vex.LogValidationErrors<AppendRecordsRequestValidator>();
	}

	[Test]
	public async ValueTask revision_checks_pass() {
		var request = CreateValidRequest();
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "some-stream",
				Revision = 5
			}
		});
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "$system-stream",
				Revision = 0
			}
		});

		var result = Validator.Validate(request);

		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async ValueTask duplicate_stream_checks_fail() {
		var request = CreateValidRequest();
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "some-stream",
				Revision = 5
			}
		});
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "some-stream",
				Revision = 5
			}
		});

		var vex = await Assert
			.That(() => Validator.ValidateAndThrow(request))
			.Throws<DetailedValidationException>();

		vex.LogValidationErrors<AppendRecordsRequestValidator>();
	}

	[Test]
	public async ValueTask duplicate_stream_case_insensitive_fails() {
		var request = CreateValidRequest();
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "Some-Stream",
				Revision = 5
			}
		});
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "some-stream",
				Revision = 10
			}
		});

		var vex = await Assert
			.That(() => Validator.ValidateAndThrow(request))
			.Throws<DetailedValidationException>();

		vex.LogValidationErrors<AppendRecordsRequestValidator>();
	}

	[Test]
	public async ValueTask any_revision_in_check_fails() {
		var request = CreateValidRequest();
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "some-stream",
				Revision = (long)ExpectedRevisionConstants.Any
			}
		});

		var vex = await Assert
			.That(() => Validator.ValidateAndThrow(request))
			.Throws<DetailedValidationException>();

		vex.LogValidationErrors<AppendRecordsRequestValidator>();
	}

	[Test]
	[Arguments((long)ExpectedRevisionConstants.NoStream)]
	[Arguments((long)ExpectedRevisionConstants.Exists)]
	[Arguments(0L)]
	[Arguments(5L)]
	[Arguments(100L)]
	public async ValueTask allowed_revision_passes(long expectedRevision) {
		var request = CreateValidRequest();
		request.ConsistencyChecks.Add(new ConsistencyCheck {
			Revision = new StreamRevisionCheck {
				Stream   = "some-stream",
				Revision = expectedRevision
			}
		});

		var result = Validator.Validate(request);

		await Assert.That(result.IsValid).IsTrue();
	}

	[Test]
	public async ValueTask no_checks_passes() {
		var request = CreateValidRequest();

		var result = Validator.Validate(request);

		await Assert.That(result.IsValid).IsTrue();
		await Assert.That(request.ConsistencyChecks).HasCount(0);
	}

	static AppendRecordsRequest CreateValidRequest() {
		var record = CreateRecord();
		record.Stream = "test-stream";
		return new AppendRecordsRequest {
			Records = { record }
		};
	}

	static AppendRecord CreateRecord() =>
		new() {
			RecordId = Guid.NewGuid().ToString(),
			Schema = new SchemaInfo {
				Name   = "TestEvent.V1",
				Format = SchemaFormat.Json
			},
			Data = ByteString.CopyFromUtf8("{}")
		};
}
