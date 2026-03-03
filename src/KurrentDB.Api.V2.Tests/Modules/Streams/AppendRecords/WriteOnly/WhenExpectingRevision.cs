// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Api.Streams;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams.Errors;
using static KurrentDB.Api.Tests.Streams.AppendRecords.AppendRecordsFixture;

namespace KurrentDB.Api.Tests.Streams.AppendRecords.WriteOnly;

public class WhenExpectingRevision {
	const long ExpectedRevision = 10L;

	[ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerTestSession)]
	public required ClusterVNodeTestContext Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask succeeds_when_stream_has_revision(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(stream, count: 11), cancellationToken: ct);

		var response = await Fixture.StreamsClient.AppendRecordsAsync(
			WriteOnlyRequest(stream, ExpectedRevision),
			cancellationToken: ct
		);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
	}

	[Test]
	public async ValueTask fails_when_stream_not_found(CancellationToken ct) {
		var stream = Fixture.NewStreamName();

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			WriteOnlyRequest(stream, ExpectedRevision),
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(stream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(ExpectedRevision);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(ActualStreamCondition.NotFound);
	}

	[Test]
	public async ValueTask fails_when_stream_is_deleted(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(stream, count: 3), cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(stream, cancellationToken: ct);

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			WriteOnlyRequest(stream, ExpectedRevision),
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(stream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(ExpectedRevision);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(ActualStreamCondition.Deleted);
	}

	[Test]
	public async ValueTask fails_when_stream_is_tombstoned(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		await Fixture.StreamsClient.AppendRecordsAsync(SeedRequest(stream), cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(stream, cancellationToken: ct);

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(
			WriteOnlyRequest(stream, ExpectedRevision),
			cancellationToken: ct
		);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(stream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(ExpectedRevision);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(ActualStreamCondition.Tombstoned);
	}
}
