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
public class SubscribeToAllTests {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_subscribing_to_all<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private const string StreamId = nameof(when_subscribing_to_all<TLogFormat, TStreamId>);
		private readonly List<ReadResp> _responses = new();
		private Position _positionOfLastWrite;

		public when_subscribing_to_all() : base(new LotsOfExpiriesStrategy()) {
		}

		protected override async Task Given() {
			var response = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamId) },
					Any = new(),
				},
				CorrelationId = Uuid.NewUuid().ToDto(),
				IsFinal = true,
				ProposedMessages = { CreateEvents(120) }
			});
			_positionOfLastWrite = new Position(response.Success.Position.CommitPosition,
				response.Success.Position.PreparePosition);
		}

		protected override async Task When() {
			using var call = StreamsClient.Read(new() {
				Options = new() {
					Subscription = new(),
					NoFilter = new(),
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
					UuidOption = new() { Structured = new() },
					All = new() {
						Start = new()
						// Position = new() {
						// 	CommitPosition = _positionOfLastWrite.CommitPosition,
						// 	PreparePosition = _positionOfLastWrite.PreparePosition
						// }
					}
				}
			}, GetCallOptions(AdminCredentials));

			_responses.AddRange(await call.ResponseStream.ReadAllAsync()
				.TakeWhile(response => response.ContentCase != ReadResp.ContentOneofCase.CaughtUp)
				.ToArrayAsync());
		}

		[Test]
		public void subscription_confirmed() {
			Assert.AreEqual(ReadResp.ContentOneofCase.Confirmation, _responses[0].ContentCase);
			Assert.NotNull(_responses[0].Confirmation.SubscriptionId);
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_subscribing_to_all_live<TLogFormat, TStreamId> : GrpcSpecification<TLogFormat, TStreamId> {
		private const string StreamId = nameof(when_subscribing_to_all_live<TLogFormat, TStreamId>);
		private readonly List<ReadResp> _responses = new();

		protected override async Task Given() {
			await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamId) },
					Any = new(),
				},
				CorrelationId = Uuid.NewUuid().ToDto(),
				IsFinal = true,
				ProposedMessages = { CreateEvents(120) }
			});
		}

		protected override async Task When() {
			using var call = StreamsClient.Read(new() {
				Options = new() {
					Subscription = new(),
					NoFilter = new(),
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
					UuidOption = new() { Structured = new() },
					All = new() { End = new() }
				}
			}, GetCallOptions(AdminCredentials));

			var positionOfLastWrite = Position.End;

			await foreach (var response in call.ResponseStream.ReadAllAsync()) {
				if (response.ContentCase == ReadResp.ContentOneofCase.Confirmation) {
					var success = (await AppendToStreamBatch(new BatchAppendReq {
						Options = new() {
							Any = new(),
							StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamId) }
						},
						CorrelationId = Uuid.NewUuid().ToDto(),
						IsFinal = true,
						ProposedMessages = { CreateEvents(1) }
					})).Success;

					positionOfLastWrite = new Position(success.Position.CommitPosition, success.Position.PreparePosition);
					_responses.Add(response);
				}
				else if (response.ContentCase == ReadResp.ContentOneofCase.Event) {
					_responses.Add(response);
					if (positionOfLastWrite <= new Position(response.Event.Event.CommitPosition, response.Event.Event.PreparePosition)) {
						break;
					}
				}
			}
		}

		[Test]
		public void subscription_confirmed() {
			Assert.AreEqual(ReadResp.ContentOneofCase.Confirmation, _responses[0].ContentCase);
			Assert.NotNull(_responses[0].Confirmation.SubscriptionId);
		}

		[Test]
		public void reads_all_the_live_events() {
			Assert.AreEqual(1,
				_responses.Count(x => x.ContentCase == ReadResp.ContentOneofCase.Event
				                      && !x.Event.Event.Metadata["type"].StartsWith("$")));
		}
	}
}
