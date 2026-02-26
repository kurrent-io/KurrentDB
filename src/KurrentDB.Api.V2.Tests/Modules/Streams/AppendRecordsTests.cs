// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf;
using Grpc.Core;
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
	public async ValueTask single_record_succeeds(CancellationToken ct) {
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
	public async ValueTask multiple_failures_reported(CancellationToken ct) {
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
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = streamA,
						ExpectedState = 999
					}
				},
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = streamB,
						ExpectedState = 888
					}
				}
			}
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(2);

		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(streamA);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(0);
		await Assert.That(details.Violations[1].StreamState.Stream).IsEqualTo(streamB);
		await Assert.That(details.Violations[1].StreamState.ActualState).IsEqualTo(1);
	}

	[Test]
	public async ValueTask tombstoned_fails(CancellationToken ct) {
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

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(stream);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(Tombstoned);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(Any);
	}

	[Test]
	public async ValueTask empty_records_fails(CancellationToken ct) {
		var request = new AppendRecordsRequest();

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.InvalidArgument);
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
	public async ValueTask revision_match_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream), CreateRecord(stream), CreateRecord(stream), CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = stream,
						ExpectedState = 3
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(4);
	}

	[Test]
	public async ValueTask revision_mismatch_fails(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream), CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = stream,
						ExpectedState = 999
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(stream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(999);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(1);
	}

	[Test]
	public async ValueTask revision_no_stream_fails(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStream = Fixture.NewStreamName(); // never created

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = checkStream,
						ExpectedState = 5
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(5);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(NoStream);
	}

	[Test]
	public async ValueTask revision_deleted_match_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(stream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = stream,
						ExpectedState = 0
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
	}

	[Test]
	public async ValueTask revision_deleted_mismatch_fails(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		var seedResponse = await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(stream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = stream,
						ExpectedState = 999
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(stream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(999);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(seedResponse.Revisions[0].Revision);
	}

	[Test]
	public async ValueTask revision_tombstoned_fails(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkStream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(checkStream, cancellationToken: ct);

		var writeStream = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = checkStream,
						ExpectedState = 0
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(0);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(Tombstoned);
	}

	[Test]
	public async ValueTask no_stream_exists_fails(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkStream), CreateRecord(checkStream), CreateRecord(checkStream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		var writeStream = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = checkStream,
						ExpectedState = NoStream
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(NoStream);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(2);
	}

	[Test]
	public async ValueTask no_stream_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = stream,
						ExpectedState = NoStream
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(0);
	}

	[Test]
	public async ValueTask no_stream_deleted_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(stream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = stream,
						ExpectedState = NoStream
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
	}

	[Test]
	public async ValueTask no_stream_tombstoned_fails(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkStream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(checkStream, cancellationToken: ct);

		var writeStream = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = checkStream,
						ExpectedState = NoStream
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(NoStream);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(Tombstoned);
	}

	[Test]
	public async ValueTask any_existing_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream), CreateRecord(stream), CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(3);
	}

	[Test]
	public async ValueTask any_new_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
		await Assert.That(response.Revisions[0].Revision).IsEqualTo(0);
	}

	[Test]
	public async ValueTask any_deleted_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(stream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
	}

	[Test]
	public async ValueTask any_tombstoned_fails(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(stream, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) }
		};

		var appendTask = async () => await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		var rex = await appendTask.ShouldThrowAsync<RpcException>();
		await Assert.That(rex.StatusCode).IsEqualTo(StatusCode.FailedPrecondition);

		var details = rex.GetRpcStatus()?.GetDetail<AppendConsistencyViolationErrorDetails>();
		await Assert.That(details).IsNotNull();
		await Assert.That(details!.Violations).HasCount(1);
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(stream);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(Tombstoned);
	}

	[Test]
	public async ValueTask exists_succeeds(CancellationToken ct) {
		var stream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(stream), CreateRecord(stream), CreateRecord(stream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(stream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = stream,
						ExpectedState = StreamExists
					}
				}
			}
		};

		var response = await Fixture.StreamsClient.AppendRecordsAsync(request, cancellationToken: ct);

		await Assert.That(response.Revisions).HasCount(1);
		await Assert.That(response.Revisions[0].Stream).IsEqualTo(stream);
	}

	[Test]
	public async ValueTask exists_no_stream_fails(CancellationToken ct) {
		var writeStream = Fixture.NewStreamName();
		var checkStream = Fixture.NewStreamName(); // never created

		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = checkStream,
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(StreamExists);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(NoStream);
	}

	[Test]
	public async ValueTask exists_deleted_fails(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkStream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.SoftDeleteStream(checkStream, cancellationToken: ct);

		var writeStream = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = checkStream,
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(StreamExists);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(SoftDeleted);
	}

	[Test]
	public async ValueTask exists_tombstoned_fails(CancellationToken ct) {
		var checkStream = Fixture.NewStreamName();
		var seedRequest = new AppendRecordsRequest {
			Records = { CreateRecord(checkStream) }
		};
		await Fixture.StreamsClient.AppendRecordsAsync(seedRequest, cancellationToken: ct);
		await Fixture.SystemClient.Management.HardDeleteStream(checkStream, cancellationToken: ct);

		var writeStream = Fixture.NewStreamName();
		var request = new AppendRecordsRequest {
			Records = { CreateRecord(writeStream) },
			Checks = {
				new ConsistencyCheck {
					StreamState = new StreamStateCheck {
						Stream   = checkStream,
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
		await Assert.That(details.Violations[0].StreamState.Stream).IsEqualTo(checkStream);
		await Assert.That(details.Violations[0].StreamState.ExpectedState).IsEqualTo(StreamExists);
		await Assert.That(details.Violations[0].StreamState.ActualState).IsEqualTo(Tombstoned);
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
