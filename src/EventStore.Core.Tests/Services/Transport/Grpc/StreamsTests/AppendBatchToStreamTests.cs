using System;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Grpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests {
	[TestFixture]
	public class AppendBatchToStreamTests {
		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class single_batch<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
			private BatchAppendResp _response;
			private readonly Uuid _correlationId;

			public single_batch() {
				_correlationId = Uuid.NewUuid();
			}

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await AppendToStreamBatch(new BatchAppendReq {
					Options = new() {
						Any = new(),
						StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("stream") }
					},
					IsFinal = true,
					ProposedMessages = { CreateEvents(1) },
					CorrelationId = _correlationId.ToDto()
				});
			}

			[Test]
			public void is_success() {
				Assert.AreEqual(_response.ResultCase, BatchAppendResp.ResultOneofCase.Success);
			}

			[Test]
			public void is_correlated() {
				Assert.AreEqual(_correlationId.ToDto(), _response.CorrelationId);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class single_batch_non_structured_uuid<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
			private BatchAppendResp _response;
			private readonly Uuid _correlationId;

			public single_batch_non_structured_uuid() {
				_correlationId = Uuid.NewUuid();
			}

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await AppendToStreamBatch(new BatchAppendReq {
					Options = new() {
						Any = new(),
						StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("stream") }
					},
					IsFinal = true,
					ProposedMessages = { CreateEvents(1) },
					CorrelationId = new() { String = _correlationId.ToString() }
				});
			}

			[Test]
			public void is_success() {
				Assert.AreEqual(_response.ResultCase, BatchAppendResp.ResultOneofCase.Success);
			}

			[Test]
			public void is_correlated() {
				Assert.AreEqual(new UUID() { String = _correlationId.ToString() }, _response.CorrelationId);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class multiple_batches<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
			private BatchAppendResp _response;
			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				var correlationId = Uuid.NewUuid();
				_response = await AppendToStreamBatch(new BatchAppendReq {
					Options = new() {
						Any = new(),
						StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("stream") }
					},
					IsFinal = false,
					ProposedMessages = { CreateEvents(1) },
					CorrelationId = correlationId.ToDto(),
				}, new() {
					IsFinal = true,
					ProposedMessages = { CreateEvents(1) },
					CorrelationId = correlationId.ToDto()
				});
			}

			[Test]
			public void is_success() {
				Assert.AreEqual(_response.ResultCase, BatchAppendResp.ResultOneofCase.Success);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class exceeded_deadline<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
			private BatchAppendResp _response;
			private readonly Uuid _correlationId;

			public exceeded_deadline() {
				_correlationId = Uuid.NewUuid();
			}

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await AppendToStreamBatch(new BatchAppendReq {
					Options = new() {
						Any = new(),
						StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("stream") },
						Deadline = Duration.FromTimeSpan(TimeSpan.Zero)
					},
					IsFinal = true,
					ProposedMessages = { CreateEvents(1) },
					CorrelationId = _correlationId.ToDto()
				});
			}

			[Test]
			public void is_error() {
				Assert.AreEqual(_response.ResultCase, BatchAppendResp.ResultOneofCase.Error);
				Assert.AreEqual(_response.Error.Code, Google.Rpc.Code.DeadlineExceeded);
			}

			[Test]
			public void is_correlated() {
				Assert.AreEqual(_correlationId.ToDto(), _response.CorrelationId);
			}
		}

		[TestFixture(typeof(LogFormat.V2), typeof(string))]
		[TestFixture(typeof(LogFormat.V3), typeof(uint))]
		public class exceeded_deadline_timestamp<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
			private BatchAppendResp _response;
			private readonly Uuid _correlationId;

			public exceeded_deadline_timestamp() {
				_correlationId = Uuid.NewUuid();
			}

			protected override Task Given() => Task.CompletedTask;

			protected override async Task When() {
				_response = await AppendToStreamBatch(new BatchAppendReq {
					Options = new() {
						Any = new(),
						StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("stream") },
						Deadline21100 = Timestamp.FromDateTime(DateTime.UtcNow)
					},
					IsFinal = true,
					ProposedMessages = { CreateEvents(1) },
					CorrelationId = _correlationId.ToDto()
				});
			}

			[Test]
			public void is_error() {
				Assert.AreEqual(_response.ResultCase, BatchAppendResp.ResultOneofCase.Error);
				Assert.AreEqual(_response.Error.Code, Google.Rpc.Code.DeadlineExceeded);
			}

			[Test]
			public void is_correlated() {
				Assert.AreEqual(_correlationId.ToDto(), _response.CorrelationId);
			}
		}

	}
}
