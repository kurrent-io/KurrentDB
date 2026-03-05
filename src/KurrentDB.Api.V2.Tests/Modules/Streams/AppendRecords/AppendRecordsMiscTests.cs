// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Api.Streams;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Testing.Bogus;
using static KurrentDB.Api.Tests.Streams.AppendRecords.AppendRecordsFixture;

namespace KurrentDB.Api.Tests.Streams.AppendRecords;

public class AppendRecordsMiscTests {
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

		await Assert.That(revA.Revision).IsEqualTo(2);
		await Assert.That(revB.Revision).IsEqualTo(1);
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
	public async ValueTask checks_with_zero_records_fails(CancellationToken ct) {
		var stream = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Checks = {
				new ConsistencyCheck {
					StreamState = new() {
						Stream        = stream,
						ExpectedState = ExpectedStreamCondition.NoStream
					}
				}
			}
		};

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	public async ValueTask duplicate_stream_in_checks_fails(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var writeStream = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new() {
						Stream        = stream,
						ExpectedState = ExpectedStreamCondition.NoStream
					}
				},
				new ConsistencyCheck {
					StreamState = new() {
						Stream        = stream,
						ExpectedState = ExpectedStreamCondition.Exists
					}
				}
			}
		};

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	public async ValueTask check_with_expected_state_any_fails(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var writeStream = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new() {
						Stream        = stream,
						ExpectedState = ExpectedStreamCondition.Any
					}
				}
			}
		};

		var act = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await act.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}
}
