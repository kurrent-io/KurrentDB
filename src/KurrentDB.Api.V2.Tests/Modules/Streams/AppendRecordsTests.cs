// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Api.Tests.Fixtures;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Protocol.V2.Streams.Errors;
using KurrentDB.Testing.Bogus;

namespace KurrentDB.Api.Tests.Streams;

public class AppendRecordsTests {
	[ClassDataSource<ClusterVNodeTestContext>(Shared = SharedType.PerTestSession)]
	public required ClusterVNodeTestContext Fixture { get; [UsedImplicitly] init; }

	[ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
	public required BogusFaker Faker { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask append_single_record_without_checks_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Position).IsGreaterThanOrEqualTo(0);
		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(0);
	}

	[Test]
	public async ValueTask interleaved_append_across_streams_tracks_revisions_independently(CancellationToken ct) {
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

		// streamA has 3 records: revision should be 2 (0-indexed)
		await Assert.That(revA.Revision).IsEqualTo(2);
		// streamB has 2 records: revision should be 1
		await Assert.That(revB.Revision).IsEqualTo(1);
	}

	[Test]
	public async ValueTask cross_stream_check_passes(CancellationToken ct) {
		// Seed stream B with some events
		var streamB = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(streamB), CreateRecord(streamB), CreateRecord(streamB) }
		};
		var seedResponse = await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		var streamBRevision = seedResponse.Revisions[0].Revision;

		// Write to stream A, check stream B
		var streamA = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(streamA) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream = streamB,
						Revision = streamBRevision
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(streamA);
	}

	[Test]
	public async ValueTask consistency_check_with_wrong_revision_reports_conflict(CancellationToken ct) {
		// Seed stream with some events
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		// Now try to write with a wrong expected revision check
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = stream,
						Revision = 999 // wrong because stream is at revision 0
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(1);
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(stream);
		await Assert.That(details.Failures[0].Revision.ExpectedRevision).IsEqualTo(999);
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(0);
	}

	[Test]
	public async ValueTask multiple_failing_consistency_checks_report_all_failures(CancellationToken ct) {
		var streamA = Fixture.NewStreamName();
		var streamB = Fixture.NewStreamName();

		// Seed both streams
		var seedA = new AppendRecordsRequest { Records = { CreateRecord(streamA) } };
		var seedB = new AppendRecordsRequest { Records = { CreateRecord(streamB), CreateRecord(streamB) } };
		await Fixture.StreamsClient.AppendRecordsAsync(seedA, cancellationToken: ct);
		await Fixture.StreamsClient.AppendRecordsAsync(seedB, cancellationToken: ct);

		var writeStream = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamA,
						Revision = 999
					}
				},
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamB,
						Revision = 888
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(2);

		// Errors are keyed by zero-based check index
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(streamA);
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(0);
		await Assert.That(details.Failures[1].Revision.Stream).IsEqualTo(streamB);
		await Assert.That(details.Failures[1].Revision.ActualRevision).IsEqualTo(1);
	}

	[Test]
	public async ValueTask check_on_nonexistent_stream_with_specific_revision_fails(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStream = Fixture.NewStreamName(); // never created

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = checkStream,
						Revision = 5
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(1);
		// -1 = stream does not exist
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(-1);
	}

	[Test]
	public async ValueTask append_to_tombstoned_stream_fails_with_failed_precondition(CancellationToken ct) {
		// Seed and tombstone a stream
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(stream, cancellationToken: ct);

		// Try to write to the tombstoned stream
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);
	}

	[Test]
	public async ValueTask tombstoned_stream_with_consistency_check_reports_tombstone_sentinel(CancellationToken ct) {
		// Seed and tombstone a stream
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(stream, cancellationToken: ct);

		// Try to write to the tombstoned stream with a consistency check
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = stream,
						Revision = 0
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(1);
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(stream);
		await Assert.That(details.Failures[0].Revision.ExpectedRevision).IsEqualTo(0);
		// -100 = stream was tombstoned
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(-100);
	}

	[Test]
	public async ValueTask tombstoned_cross_stream_check_reports_tombstone_sentinel(CancellationToken ct) {
		// Seed and tombstone stream B
		var streamB = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(streamB) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(streamB, cancellationToken: ct);

		// Write to stream A, check tombstoned stream B
		var streamA = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(streamA) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamB,
						Revision = 0
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(1);
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(streamB);
		await Assert.That(details.Failures[0].Revision.ExpectedRevision).IsEqualTo(0);
		// -100 = stream was tombstoned
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(-100);
	}

	[Test]
	public async ValueTask soft_deleted_stream_with_exists_check_reports_soft_delete_sentinel(CancellationToken ct) {
		// Seed and soft-delete a stream
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(stream, cancellationToken: ct);

		// Try to write to the soft-deleted stream with an Exists consistency check
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = stream,
						Revision = -4 // Exists
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(1);
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(stream);
		await Assert.That(details.Failures[0].Revision.ExpectedRevision).IsEqualTo(-4);
		// -10 = stream was soft-deleted
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(-10);
	}

	[Test]
	public async ValueTask soft_deleted_cross_stream_exists_check_reports_soft_delete_sentinel(CancellationToken ct) {
		// Seed and soft-delete stream B
		var streamB = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(streamB) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(streamB, cancellationToken: ct);

		// Write to stream A, check soft-deleted stream B with Exists
		var streamA = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(streamA) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamB,
						Revision = -4 // Exists
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(1);
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(streamB);
		await Assert.That(details.Failures[0].Revision.ExpectedRevision).IsEqualTo(-4);
		// -10 = stream was soft-deleted
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(-10);
	}

	[Test, Skip("Core does not yet report per-stream failure types for soft-deleted streams.")]
	public async ValueTask multiple_soft_deleted_cross_stream_checks_report_all_failures(CancellationToken ct) {
		// Seed and soft-delete two streams
		var streamB = Fixture.NewStreamName();
		var streamC = Fixture.NewStreamName();
		var seedB = new AppendRecordsRequest { Records = { CreateRecord(streamB) } };
		var seedC = new AppendRecordsRequest { Records = { CreateRecord(streamC) } };
		await Fixture.StreamsClient.AppendRecordsAsync(seedB, cancellationToken: ct);
		await Fixture.StreamsClient.AppendRecordsAsync(seedC, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(streamB, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(streamC, cancellationToken: ct);

		// Write to stream A, check both soft-deleted streams with Exists
		var streamA = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(streamA) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamB,
						Revision = -4 // Exists
					}
				},
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamC,
						Revision = -4 // Exists
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(2);
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(streamB);
		// -10 = stream was soft-deleted
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(-10);
		await Assert.That(details.Failures[1].Revision.Stream).IsEqualTo(streamC);
		// -10 = stream was soft-deleted
		await Assert.That(details.Failures[1].Revision.ActualRevision).IsEqualTo(-10);
	}

	[Test, Skip("Core does not yet report per-stream failure types for soft-deleted streams.")]
	public async ValueTask soft_deleted_and_wrong_revision_checks_report_per_stream_failures(CancellationToken ct) {
		// Seed stream B and soft-delete it; seed stream C normally
		var streamB = Fixture.NewStreamName();
		var streamC = Fixture.NewStreamName();
		var seedB = new AppendRecordsRequest { Records = { CreateRecord(streamB) } };
		var seedC = new AppendRecordsRequest { Records = { CreateRecord(streamC) } };
		await Fixture.StreamsClient.AppendRecordsAsync(seedB, cancellationToken: ct);
		await Fixture.StreamsClient.AppendRecordsAsync(seedC, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(streamB, cancellationToken: ct);

		// Write to stream A, check soft-deleted stream B (Exists) and stream C with wrong revision
		var streamA = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(streamA) },
			ConsistencyChecks = {
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamB,
						Revision = -4 // Exists
					}
				},
				new ConsistencyCheck {
					Revision = new StreamRevisionCheck {
						Stream   = streamC,
						Revision = 999 // wrong: stream C is at revision 0
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<ConsistencyCheckFailedErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Failures).HasCount(2);
		// Stream B was soft-deleted: should report -10
		await Assert.That(details.Failures[0].Revision.Stream).IsEqualTo(streamB);
		await Assert.That(details.Failures[0].Revision.ActualRevision).IsEqualTo(-10);
		// Stream C has wrong revision: should report the actual revision (0)
		await Assert.That(details.Failures[1].Revision.Stream).IsEqualTo(streamC);
		await Assert.That(details.Failures[1].Revision.ActualRevision).IsEqualTo(0);
	}

	[Test]
	public async ValueTask empty_records_throws_invalid_argument(CancellationToken ct) {
		var request = new AppendRecordsRequest();

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
	}

	[Test]
	[Repeat(10)]
	public async ValueTask oversized_record_throws_invalid_argument(CancellationToken ct) {
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
