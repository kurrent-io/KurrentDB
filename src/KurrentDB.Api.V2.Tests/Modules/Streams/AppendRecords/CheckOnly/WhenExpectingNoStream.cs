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
public class WhenExpectingNoStream {
	[ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerTestSession)]
	public required ClusterVNodeTestContext Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask fails_when_stream_has_revision(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var writeStream = Fixture.NewStreamName();
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(checkStream, count: 3), cancellationToken: ct);

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new () {
							Stream        = checkStream,
							ExpectedState = ExpectedStreamCondition.NoStream
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
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(ExpectedStreamCondition.NoStream);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(2L);
	}

	[Test]
	public async ValueTask succeeds_when_stream_not_found(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var writeStream = Fixture.NewStreamName();

		var response = await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new () {
							Stream        = checkStream,
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
	public async ValueTask succeeds_when_stream_is_deleted(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var writeStream = Fixture.NewStreamName();
		await SeedDeletedStream(Fixture, checkStream, ct: ct);

		var response = await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new () {
							Stream        = checkStream,
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
	public async ValueTask succeeds_when_stream_is_tombstoned(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var writeStream = Fixture.NewStreamName();
		await SeedTombstonedStream(Fixture, checkStream, ct: ct);

		var response = await Fixture.StreamsClient.AppendRecordsAsync(
			new AppendRecordsRequest {
				Records = { CreateRecord(writeStream) },
				Checks = {
					new ConsistencyCheck {
						StreamState = new () {
							Stream        = checkStream,
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
}
