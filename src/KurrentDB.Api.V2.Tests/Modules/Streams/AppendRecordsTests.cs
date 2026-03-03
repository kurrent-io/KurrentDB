// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Api.Streams;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Protocol.V2.Streams.Errors;
using KurrentDB.Testing.Bogus;
using static KurrentDB.Core.Data.ExpectedVersion;
using static KurrentDB.Protocol.V2.Streams.ConsistencyCheck.Types;

namespace KurrentDB.Api.Tests.Streams;

public class AppendRecordsTests {
	private const long SoftDeleted = -10;
	private const long Tombstoned = -100;

	[ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerTestSession)]
	public required ClusterVNodeTestContext Fixture { get; [UsedImplicitly] init; }

	[ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
	public required BogusFaker Faker { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask interleaved_tracks_revisions(CancellationToken ct) {
		var streamA = Fixture.NewStreamName();
		var streamB = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Records = {
				CreateRecord(streamA),
				CreateRecord(streamB),
				CreateRecord(streamA),
				CreateRecord(streamB),
				CreateRecord(streamA)
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(2);

		var revA = response.Revisions.First(r => r.Stream == streamA);
		var revB = response.Revisions.First(r => r.Stream == streamB);

		await Assert.That(revA.Revision).IsEqualTo(2); // streamA has 3 records
		await Assert.That(revB.Revision).IsEqualTo(1); // streamB has 2 records
	}

	[Test]
	[Repeat(10)]
	public async ValueTask oversized_record_fails(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var record = CreateRecord(stream);

		var recordSize = (int)(Fixture.ServerOptions.Application.MaxAppendEventSize * Faker.Random.Double(1.01, 1.04));
		record.Data = UnsafeByteOperations.UnsafeWrap(Faker.Random.Bytes(recordSize));

		var request = new AppendRecordsRequest {
			Records = { record }
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	public async ValueTask multiple_checks_only_no_stream_violates_expected_state(CancellationToken ct) {
		var checkOnlyStream1 = Fixture.NewStreamName();
		var checkOnlyStream2 = Fixture.NewStreamName();
		var writeOnlyStream  = Fixture.NewStreamName();

		ConsistencyCheck revisionCheck     = new() { StreamState = new StreamStateCheck { Stream = checkOnlyStream1, ExpectedState = 10L } };
		ConsistencyCheck streamExistsCheck = new() { StreamState = new StreamStateCheck { Stream = checkOnlyStream2, ExpectedState = StreamExists } };

		ConsistencyCheck[] checks = [revisionCheck, streamExistsCheck];

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = { revisionCheck, streamExistsCheck }
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(2);

		using (Assert.Multiple()) {
			foreach (var (violation, check) in details.Violations.Zip(checks)) {
				await Assert.That(violation.StreamState.Stream).IsEqualTo(check.StreamState.Stream);
				await Assert.That(violation.StreamState.ExpectedState).IsEqualTo(check.StreamState.ExpectedState);
				await Assert.That(violation.StreamState.ActualState).IsEqualTo(NoStream);
			}
		}
	}

	[Test]
	public async ValueTask multiple_checks_only_tombstoned_violates_expected_state(CancellationToken ct) {
		var checkOnlyStream1 = Fixture.NewStreamName();
		var checkOnlyStream2 = Fixture.NewStreamName();
		var checkOnlyStream3 = Fixture.NewStreamName();
		var writeOnlyStream  = Fixture.NewStreamName();

		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkOnlyStream1), CreateRecord(checkOnlyStream2), CreateRecord(checkOnlyStream3) }
		};

		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(checkOnlyStream1, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(checkOnlyStream2, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(checkOnlyStream3, cancellationToken: ct);

		ConsistencyCheck revisionCheck     = new() { StreamState = new StreamStateCheck { Stream = checkOnlyStream1, ExpectedState = 10L } };
		ConsistencyCheck streamExistsCheck = new() { StreamState = new StreamStateCheck { Stream = checkOnlyStream2, ExpectedState = StreamExists } };
		ConsistencyCheck noStreamCheck     = new() { StreamState = new StreamStateCheck { Stream = checkOnlyStream3, ExpectedState = NoStream } };

		ConsistencyCheck[] checks = [revisionCheck, streamExistsCheck, noStreamCheck];

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = { revisionCheck, streamExistsCheck, noStreamCheck }
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(3);

		using (Assert.Multiple()) {
			foreach (var (violation, check) in details.Violations.Zip(checks)) {
				await Assert.That(violation.StreamState.Stream).IsEqualTo(check.StreamState.Stream);
				await Assert.That(violation.StreamState.ExpectedState).IsEqualTo(check.StreamState.ExpectedState);
				await Assert.That(violation.StreamState.ActualState).IsEqualTo(Tombstoned);
			}
		}
	}

	[Test]
	[Arguments(StreamExists)]
	[Arguments(10L)]
	public async ValueTask single_check_only_no_stream_violates_expected_state(long expectedState, CancellationToken ct) {
		var checkOnlyStream = Fixture.NewStreamName();
		var writeOnlyStream = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream = checkOnlyStream,
						ExpectedState = expectedState
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkOnlyStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(expectedState);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(NoStream);
	}

	[Test]
	[Arguments(10L)]
	[Arguments(StreamExists)]
	[Arguments(NoStream)]
	public async ValueTask single_check_only_tombstoned_violates_expected_state(long expectedState, CancellationToken ct) {
		var checkOnlyStream = Fixture.NewStreamName();
		var writeOnlyStream = Fixture.NewStreamName();

		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkOnlyStream) }
		};

		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(checkOnlyStream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream        = checkOnlyStream,
						ExpectedState = expectedState
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkOnlyStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(expectedState);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(Tombstoned);
	}

	[Test]
	[Arguments(StreamExists)]
	[Arguments(10L)]
	public async ValueTask single_check_only_revision_satistifes_expected_state(long expectedState, CancellationToken ct) {
		var checkOnlyStream = Fixture.NewStreamName();
		var writeOnlyStream = Fixture.NewStreamName();

		var seedRequest = new AppendRecordsRequest {
			Records = { Enumerable.Range(0, 11).Select(_ => CreateRecord(checkOnlyStream)) }
		};

		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream = checkOnlyStream,
						ExpectedState = expectedState
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);
		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(writeOnlyStream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask single_check_only_revision_violates_no_stream(CancellationToken ct) {
		var checkOnlyStream = Fixture.NewStreamName();
		var writeOnlyStream = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream = checkOnlyStream,
						ExpectedState = 10L
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkOnlyStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(10L);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(NoStream);
	}

	[Test]
	public async ValueTask single_check_only_deleted_violates_expected_state(CancellationToken ct) {
		var checkOnlyStream = Fixture.NewStreamName();
		var writeOnlyStream = Fixture.NewStreamName();

		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkOnlyStream), CreateRecord(checkOnlyStream), CreateRecord(checkOnlyStream) }
		};
		var seedResponse = await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(checkOnlyStream, cancellationToken: ct);
		await Assert.That(seedResponse.Revisions).HasCount(1);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream = checkOnlyStream,
						ExpectedState = 10L
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkOnlyStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(10L);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(seedResponse.Revisions.Last().Revision);
	}

	[Test]
	public async ValueTask single_check_only_deleted_violates_stream_exists(CancellationToken ct) {
		var checkOnlyStream = Fixture.NewStreamName();
		var writeOnlyStream = Fixture.NewStreamName();

		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkOnlyStream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(checkOnlyStream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream = checkOnlyStream,
						ExpectedState = StreamExists
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkOnlyStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(StreamExists);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(SoftDeleted);
	}

	[Test]
	public async ValueTask soft_deleted_stream_check_passes_with_correct_revision(CancellationToken ct) {
		var checkOnlyStream = Fixture.NewStreamName();
		var writeOnlyStream = Fixture.NewStreamName();

		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkOnlyStream), CreateRecord(checkOnlyStream), CreateRecord(checkOnlyStream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		await Fixture.SystemClient.Management.SoftDeleteStream(checkOnlyStream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeOnlyStream) },
			Checks  = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream = checkOnlyStream,
						ExpectedState = ExpectedStreamCondition.Deleted
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);
		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(writeOnlyStream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(0L);
	}

	static AppendRecord CreateRecord(string stream) =>
		new() {
			Stream   = stream,
			RecordId = Guid.NewGuid().ToString(),
			Schema = new SchemaInfo {
				Name   = "TestEvent.V1",
				Format = SchemaFormat.Json
			},
			Data = ByteString.CopyFromUtf8("{\"test\": true}")
		};
}
