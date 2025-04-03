// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Common;
using EventStore.Core.Services.Transport.Grpc;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;

[TestFixture]
public class ReadAllBackwardsTests {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_reading_all_backwards<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private const string StreamId = nameof(when_reading_all_backwards<TLogFormat, TStreamId>);

		private readonly List<ReadResp> _responses = new();
		private Position _positionOfLastWrite;

		protected override async Task Given() {
			var response = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamId) },
					Any = new()
				},
				CorrelationId = Uuid.NewUuid().ToDto(),
				IsFinal = true,
				ProposedMessages = { CreateEvents(50) }
			});

			_positionOfLastWrite = new Position(response.Success.Position.CommitPosition,
				response.Success.Position.PreparePosition);
		}

		protected override async Task When() {
			using var call = StreamsClient.Read(new() {
				Options = new() {
					UuidOption = new() { Structured = new() },
					Count = 20,
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Backwards,
					ResolveLinks = false,
					All = new() {
						Position = new() {
							CommitPosition = _positionOfLastWrite.CommitPosition,
							PreparePosition = _positionOfLastWrite.PreparePosition
						}
					},
					NoFilter = new()
				}
			}, GetCallOptions(AdminCredentials));
			_responses.AddRange(await call.ResponseStream.ReadAllAsync().ToArrayAsync());
		}

		[Test]
		public void should_not_receive_null_events() {
			Assert.False(_responses
				.Where(x => x.ContentCase == ReadResp.ContentOneofCase.Event)
				.Any(x => x.Event is null));
		}

		[Test]
		public void should_read_a_number_of_events_equal_to_the_max_count() {
			Assert.AreEqual(20, _responses.Count(x => x.Event is not null));
		}

		[Test]
		public void should_read_the_correct_events() {
			Assert.True(_responses
				.Where(x => x.Event is not null)
				.All(x =>
					new Position(x.Event.Event.CommitPosition, x.Event.Event.PreparePosition) <=
					_positionOfLastWrite));
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_reading_all_backwards_from_end<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private readonly List<ReadResp> _responses = new();
		private const string StreamId = nameof(when_reading_all_backwards_from_end<TLogFormat, TStreamId>);

		protected override async Task Given() {
			await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamId) },
					Any = new()
				},
				CorrelationId = Uuid.NewUuid().ToDto(),
				IsFinal = true,
				ProposedMessages = { CreateEvents(50) }
			});
		}

		protected override async Task When() {
			using var call = StreamsClient.Read(new() {
				Options = new() {
					UuidOption = new() { Structured = new() },
					Count = 20,
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Backwards,
					ResolveLinks = false,
					All = new() { End = new() },
					NoFilter = new()
				}
			}, GetCallOptions(AdminCredentials));
			_responses.AddRange(await call.ResponseStream.ReadAllAsync().ToArrayAsync());
		}

		[Test]
		public void should_not_receive_null_events() {
			Assert.False(_responses
				.Where(x => x.ContentCase == ReadResp.ContentOneofCase.Event)
				.Any(x => x.Event is null));
		}

		[Test]
		public void should_read_a_number_of_events_equal_to_the_max_count() {
			Assert.AreEqual(20, _responses.Count(x => x.Event is not null));
		}

		[Test]
		public void should_read_the_correct_events() {
			Assert.AreEqual(49, _responses.First(x => x.Event is not null).Event.Event.StreamRevision);
			Assert.AreEqual(30, _responses.Last(x => x.Event is not null).Event.Event.StreamRevision);
		}
	}
}
