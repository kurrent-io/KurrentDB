// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Google.Protobuf.WellKnownTypes;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Grpc;
using Google.Protobuf;
using Google.Rpc;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;

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
	public class single_batch_with_too_large_events<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private BatchAppendResp _response;
		private readonly Uuid _correlationId;
		private const int MaxAppendEventSize = 1024;
		private BatchAppendReq.Types.ProposedMessage _tooLargeEvent;

		public single_batch_with_too_large_events() : base(maxAppendEventSize: MaxAppendEventSize) {
			_correlationId = Uuid.NewUuid();
		}

		protected override Task Given() => Task.CompletedTask;

		protected override async Task When() {
			_tooLargeEvent = CreateEvent(dataSize: MaxAppendEventSize, metadataSize: MaxAppendEventSize);
			_response = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("stream") }
				},
				IsFinal = true,
				ProposedMessages = { CreateEvent(), _tooLargeEvent, CreateEvent() },
				CorrelationId = _correlationId.ToDto()
			});
		}

		[Test]
		public void is_error() {
			Assert.AreEqual(BatchAppendResp.ResultOneofCase.Error, _response.ResultCase);
			Assert.AreEqual(Code.InvalidArgument, _response.Error.Code);
		}

		[Test]
		public void is_correlated() {
			Assert.AreEqual(_correlationId.ToDto(), _response.CorrelationId);
		}

		[Test]
		public void contains_error_details() {
			Assert.True(_response.Error.Details.Is(MaximumAppendEventSizeExceeded.Descriptor));
			var details = _response.Error.Details.Unpack<MaximumAppendEventSizeExceeded>();
			Assert.AreEqual(Uuid.FromDto(_tooLargeEvent.Id).ToString(), details.EventId);
			Assert.AreEqual(MaxAppendEventSize * 2 + 2, details.ProposedEventSize);
			Assert.AreEqual(MaxAppendEventSize, details.MaxAppendEventSize);
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
				CorrelationId = new() {String = _correlationId.ToString()}
			});
		}

		[Test]
		public void is_success() {
			Assert.AreEqual(_response.ResultCase, BatchAppendResp.ResultOneofCase.Success);
		}

		[Test]
		public void is_correlated() {
			Assert.AreEqual(new UUID() {String = _correlationId.ToString()}, _response.CorrelationId);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	public class multiple_batches_with_one_having_too_large_events<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private BatchAppendResp _response;
		private const int MaxAppendEventSize = 1024;
		private BatchAppendReq.Types.ProposedMessage _tooLargeEvent;

		public multiple_batches_with_one_having_too_large_events()
			: base(maxAppendEventSize: MaxAppendEventSize) {
		}

		protected override Task Given() => Task.CompletedTask;

		protected override async Task When() {
			_tooLargeEvent = CreateEvent(dataSize: MaxAppendEventSize, metadataSize: MaxAppendEventSize);
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
				ProposedMessages = { _tooLargeEvent },
				CorrelationId = correlationId.ToDto()
			});
		}

		[Test]
		public void is_error() {
			Assert.AreEqual(BatchAppendResp.ResultOneofCase.Error, _response.ResultCase);
			Assert.AreEqual(Code.InvalidArgument, _response.Error.Code);
		}

		[Test]
		public void contains_error_details() {
			Assert.True(_response.Error.Details.Is(MaximumAppendEventSizeExceeded.Descriptor));
			var details = _response.Error.Details.Unpack<MaximumAppendEventSizeExceeded>();
			Assert.AreEqual(Uuid.FromDto(_tooLargeEvent.Id).ToString(), details.EventId);
			Assert.AreEqual(MaxAppendEventSize * 2 + 2, details.ProposedEventSize);
			Assert.AreEqual(MaxAppendEventSize, details.MaxAppendEventSize);
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
					StreamIdentifier = new() {StreamName = ByteString.CopyFromUtf8("stream")},
					Deadline = Duration.FromTimeSpan(TimeSpan.Zero)
				},
				IsFinal = true,
				ProposedMessages = {CreateEvents(1)},
				CorrelationId = _correlationId.ToDto()
			});
		}

		[Test]
		public void is_error() {
			Assert.AreEqual(_response.ResultCase, BatchAppendResp.ResultOneofCase.Error);
			Assert.AreEqual(_response.Error.Code, Code.DeadlineExceeded);
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
			Assert.AreEqual(_response.Error.Code, Code.DeadlineExceeded);
		}

		[Test]
		public void is_correlated() {
			Assert.AreEqual(_correlationId.ToDto(), _response.CorrelationId);
		}
	}

}
