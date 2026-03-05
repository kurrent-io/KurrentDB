// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Api.Streams;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Protocol.V2.Streams.Errors;
using static KurrentDB.Api.Tests.Streams.AppendRecords.AppendRecordsFixture;

namespace KurrentDB.Api.Tests.Streams.AppendRecords.CheckOnly;

[Category("AppendRecords")]
public class WhenMultipleChecks {
	[ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerTestSession)]
	public required ClusterVNodeTestContext Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask succeeds_when_all_checks_pass(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStreamA = Fixture.NewStreamName();
		var checkStreamB = Fixture.NewStreamName();

		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(checkStreamA, count: 3), cancellationToken: ct);
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(checkStreamB, count: 5), cancellationToken: ct);

		var response = await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamA,
							ExpectedState = 2L
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamB,
							ExpectedState = 4L
						}
					}
				}
			},
			cancellationToken: ct
		);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(writeStream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask succeeds_when_all_mixed_check_types_pass(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var revisionStream = Fixture.NewStreamName();
		var existsStream = Fixture.NewStreamName();
		var noStream = Fixture.NewStreamName();

		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(revisionStream, count: 3), cancellationToken: ct);
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(existsStream, count: 1), cancellationToken: ct);

		var response = await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = revisionStream,
							ExpectedState = 2L
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = existsStream,
							ExpectedState = ExpectedStreamCondition.Exists
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = noStream,
							ExpectedState = ExpectedStreamCondition.NoStream
						}
					}
				}
			},
			cancellationToken: ct
		);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(writeStream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask fails_when_one_of_multiple_checks_fails(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStreamA = Fixture.NewStreamName();
		var checkStreamB = Fixture.NewStreamName();

		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(checkStreamA, count: 3), cancellationToken: ct);
		// checkStreamB not seeded — will fail the revision check

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamA,
							ExpectedState = 2L
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamB,
							ExpectedState = 5L
						}
					}
				}
			},
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].CheckIndex).IsEqualTo(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStreamB);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(5L);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);
	}

	[Test]
	public async ValueTask fails_when_all_checks_fail(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStreamA = Fixture.NewStreamName();
		var checkStreamB = Fixture.NewStreamName();

		// Neither stream is seeded — both will fail

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamA,
							ExpectedState = 3L
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamB,
							ExpectedState = ExpectedStreamCondition.Exists
						}
					}
				}
			},
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(2);

		var violationA = details.Violations.First(v => v.StreamState.Stream == checkStreamA);
		var violationB = details.Violations.First(v => v.StreamState.Stream == checkStreamB);

		await Assert.That(violationA.CheckIndex).IsEqualTo(0);
		await Assert.That(violationA.StreamState.ExpectedState).IsEqualTo(3L);
		await Assert.That(violationA.StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);

		await Assert.That(violationB.CheckIndex).IsEqualTo(1);
		await Assert.That(violationB.StreamState.ExpectedState).IsEqualTo(ExpectedStreamCondition.Exists);
		await Assert.That(violationB.StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);
	}

	[Test]
	public async ValueTask fails_with_mixed_violation_states(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var deletedStream = Fixture.NewStreamName();
		var tombstonedStream = Fixture.NewStreamName();
		var missingStream = Fixture.NewStreamName();

		await SeedDeletedStream(Fixture, deletedStream, count: 3, ct: ct);
		await SeedTombstonedStream(Fixture, tombstonedStream, ct: ct);
		// missingStream not seeded

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = deletedStream,
							ExpectedState = ExpectedStreamCondition.Exists
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = tombstonedStream,
							ExpectedState = ExpectedStreamCondition.Exists
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = missingStream,
							ExpectedState = 10L
						}
					}
				}
			},
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(3);

		var deletedViolation = details.Violations.First(v => v.StreamState.Stream == deletedStream);
		var tombstonedViolation = details.Violations.First(v => v.StreamState.Stream == tombstonedStream);
		var missingViolation = details.Violations.First(v => v.StreamState.Stream == missingStream);

		await Assert.That(deletedViolation.CheckIndex).IsEqualTo(0);
		await Assert.That(deletedViolation.StreamState.ActualState).IsEqualTo(ActualStreamCondition.Deleted);
		await Assert.That(tombstonedViolation.CheckIndex).IsEqualTo(1);
		await Assert.That(tombstonedViolation.StreamState.ActualState).IsEqualTo(ActualStreamCondition.Tombstoned);
		await Assert.That(missingViolation.CheckIndex).IsEqualTo(2);
		await Assert.That(missingViolation.StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);
	}

	[Test]
	public async ValueTask fails_when_first_check_fails_and_second_passes(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStreamA = Fixture.NewStreamName();
		var checkStreamB = Fixture.NewStreamName();

		// checkStreamA not seeded — will fail the revision check
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(checkStreamB, count: 6), cancellationToken: ct);

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamA,
							ExpectedState = 3L
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamB,
							ExpectedState = 5L
						}
					}
				}
			},
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].CheckIndex).IsEqualTo(0);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStreamA);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(3L);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);
	}

	[Test]
	public async ValueTask fails_when_two_of_three_checks_fail(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStreamA = Fixture.NewStreamName();
		var checkStreamB = Fixture.NewStreamName();
		var checkStreamC = Fixture.NewStreamName();

		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(checkStreamA, count: 3), cancellationToken: ct);
		// checkStreamB not seeded — will fail
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(checkStreamC, count: 2), cancellationToken: ct);

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamA,
							ExpectedState = 2L
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamB,
							ExpectedState = ExpectedStreamCondition.Exists
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStreamC,
							ExpectedState = 10L
						}
					}
				}
			},
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(2);

		var violationB = details.Violations.First(v => v.StreamState.Stream == checkStreamB);
		var violationC = details.Violations.First(v => v.StreamState.Stream == checkStreamC);

		await Assert.That(violationB.CheckIndex).IsEqualTo(1);
		await Assert.That(violationB.StreamState.ExpectedState).IsEqualTo(ExpectedStreamCondition.Exists);
		await Assert.That(violationB.StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);

		await Assert.That(violationC.CheckIndex).IsEqualTo(2);
		await Assert.That(violationC.StreamState.ExpectedState).IsEqualTo(10L);
		await Assert.That(violationC.StreamState.ActualState).IsEqualTo(1L);
	}

	[Test]
	public async ValueTask fails_when_check_on_write_target_and_separate_check_fails(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var writeStream = Fixture.NewStreamName();

		// checkStream not seeded — will fail
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(writeStream, count: 4), cancellationToken: ct);

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = checkStream,
							ExpectedState = 5L
						}
					},
					new ConsistencyCheck {
						StreamState = new() {
							Stream        = writeStream,
							ExpectedState = 3L
						}
					}
				}
			},
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].CheckIndex).IsEqualTo(0);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(5L);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);
	}
}
