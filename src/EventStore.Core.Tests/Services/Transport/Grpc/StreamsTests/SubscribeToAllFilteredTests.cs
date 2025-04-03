// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Grpc;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using Position = EventStore.Core.Services.Transport.Common.Position;
using GrpcMetadata = EventStore.Core.Services.Transport.Grpc.Constants.Metadata;
using LogV3StreamId = System.UInt32;

namespace EventStore.Core.Tests.Services.Transport.Grpc.StreamsTests;

[TestFixture]
public class SubscribeToAllFilteredTests {
	public static IEnumerable<object[]> TestCases() {
		var checkpointIntervalMultipliers = new uint[] { 2, 4, 8 };

		var maxSearchWindows = new uint[] { 1, 32, 64 };

		var filteredEventCount = checkpointIntervalMultipliers.Max() * maxSearchWindows.Max();

		var logFormats = new[] {
			(typeof(LogFormat.V2), typeof(string)),
			(typeof(LogFormat.V3), typeof(LogV3StreamId)),
		};

		return from checkpointInterval in checkpointIntervalMultipliers
			from maxSearchWindow in maxSearchWindows
			from logFormat in logFormats
			select new object[] {
				logFormat.Item1,
				logFormat.Item2,
				checkpointInterval,
				maxSearchWindow,
				(int)filteredEventCount
			};
	}

	[TestFixtureSource(typeof(SubscribeToAllFilteredTests), nameof(TestCases))]
	public class when_subscribing_to_all_with_a_filter<TLogFormat, TStreamId>
		: GrpcSpecification<TLogFormat, TStreamId> {
		private const string StreamName = "test";

		private int CheckpointCount => _positions.Count;

		private readonly uint _maxSearchWindow;
		private readonly int _filteredEventCount;
		private readonly uint _checkpointIntervalMultiplier;
		private readonly List<Position> _positions;
		private readonly uint _checkpointInterval;

		private Position _position;
		private long _expected;

		public when_subscribing_to_all_with_a_filter(uint checkpointIntervalMultiplier, uint maxSearchWindow,
			int filteredEventCount)
			: base (new LotsOfExpiriesStrategy()) {
			_maxSearchWindow = maxSearchWindow;
			_checkpointIntervalMultiplier = checkpointIntervalMultiplier;
			_checkpointInterval = checkpointIntervalMultiplier * maxSearchWindow;
			_filteredEventCount = filteredEventCount;
			_positions = new List<Position>();
			_position = Position.End;
		}

		protected override async Task Given() {
			await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8("abcd") }
				},
				IsFinal = true,
				ProposedMessages = { CreateEvents(_filteredEventCount) },
				CorrelationId = Uuid.NewUuid().ToDto()
			});
		}

		protected override async Task When() {
			var success = (await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamName) }
				},
				IsFinal = true,
				ProposedMessages = { CreateEvents(1) },
				CorrelationId = Uuid.NewUuid().ToDto()
			})).Success;

			_position = new Position(success.Position.CommitPosition, success.Position.PreparePosition);

			using var skippedEventCall = StreamsClient.Read(new ReadReq {
				Options = new ReadReq.Types.Options {
					Count = 4096,
					All = new() { Start = new() },
					UuidOption = new() { Structured = new() },
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
					NoFilter = new()
				}
			}, GetCallOptions(AdminCredentials));
			var skippedEventCount = await skippedEventCall.ResponseStream.ReadAllAsync().CountAsync(response =>
				response.Event is not null &&
				new Position(response.Event.Event.CommitPosition, response.Event.Event.PreparePosition) <=
				_position &&
				response.Event.Event.StreamIdentifier.StreamName.ToStringUtf8() != StreamName);

			// skip the epochinfo - which isn't in unfiltered all reads so we haven't accounted for it above
			skippedEventCount += 1;
			_expected = skippedEventCount / _checkpointInterval;

			using var call = StreamsClient.Read(new ReadReq {
				Options = new ReadReq.Types.Options {
					Subscription = new(),
					All = new() { Start = new() },
					Filter = new() {
						Max = _maxSearchWindow,
						CheckpointIntervalMultiplier = _checkpointIntervalMultiplier,
						StreamIdentifier = new() { Prefix = { StreamName } }
					},
					UuidOption = new() { Structured = new() },
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards
				}
			}, GetCallOptions(AdminCredentials));

			var receivedTheEvent = false;
			await foreach (var response in call.ResponseStream.ReadAllAsync()) {
				if (response.ContentCase == ReadResp.ContentOneofCase.CaughtUp) {
					// we have successfully transitioned to live (and will receive no more events)
					break;
				}

				if (response.ContentCase == ReadResp.ContentOneofCase.Checkpoint) {
					if (receivedTheEvent) {
						break;
					}
					_positions.Add(new Position(response.Checkpoint.CommitPosition,
						response.Checkpoint.PreparePosition));
					continue;
				}

				if (response.ContentCase == ReadResp.ContentOneofCase.Event) {
					Assert.AreEqual(StreamName, response.Event.Event.StreamIdentifier.StreamName.ToStringUtf8());
					receivedTheEvent = true;
				}
			}
		}

		[Test]
		public void receives_the_correct_number_of_checkpoints() {
			Assert.AreEqual(_expected, CheckpointCount);
		}

		[Test]
		public void no_duplicate_checkpoints_received() {
			Assert.AreEqual(_positions.Distinct().Count(), _positions.Count);
		}
	}

	[TestFixtureSource(typeof(SubscribeToAllFilteredTests), nameof(TestCases))]
	public class when_subscribing_to_all_with_a_filter_live<TLogFormat, TStreamId>
		: GrpcSpecification<TLogFormat, TStreamId> {
		private const string StreamName = "test";

		private int CheckpointCount => _positions.Count;

		private readonly uint _maxSearchWindow;
		private readonly int _filteredEventCount;
		private readonly uint _checkpointIntervalMultiplier;
		private readonly List<Position> _positions;
		private readonly uint _checkpointInterval;

		private Position _position;

		public when_subscribing_to_all_with_a_filter_live(uint checkpointIntervalMultiplier, uint maxSearchWindow,
			int filteredEventCount) {
			_maxSearchWindow = maxSearchWindow;
			_checkpointIntervalMultiplier = checkpointIntervalMultiplier;
			_checkpointInterval = checkpointIntervalMultiplier * maxSearchWindow;
			_filteredEventCount = filteredEventCount;
			_positions = new List<Position>();
			_position = Position.End;
		}

		protected override async Task Given() {
			await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() {StreamName = ByteString.CopyFromUtf8("abcd")}
				},
				IsFinal = true,
				ProposedMessages = {CreateEvents(_filteredEventCount)},
				CorrelationId = Uuid.NewUuid().ToDto()
			});
		}

		protected override async Task When() {
			for (int i = 1; i <= 5; i++) {
				try {
					await WhenWhichMightConsumeTooSlow(i);
					return;
				} catch (RpcException ex) when(ex.Message.Contains("too slow")) {
					_positions.Clear();
					await Task.Delay(500);
				}
			}
		}

		private async Task WhenWhichMightConsumeTooSlow(int attempt) {
			var streamName = attempt + StreamName;
			using var call = StreamsClient.Read(new ReadReq {
				Options = new ReadReq.Types.Options {
					Subscription = new(),
					All = new() {End = new()},
					Filter = new() {
						Max = _maxSearchWindow,
						CheckpointIntervalMultiplier = _checkpointIntervalMultiplier,
						StreamIdentifier = new() {Prefix = {streamName}}
					},
					UuidOption = new() {Structured = new()},
					ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards
				}
			}, GetCallOptions(AdminCredentials));

			Assert.True(await call.ResponseStream.MoveNext());

			Assert.AreEqual(ReadResp.ContentOneofCase.Confirmation, call.ResponseStream.Current.ContentCase);

			var success = new BatchAppendResp.Types.Success();
			for (int i = 0; i <= _checkpointInterval ; i++) {
				success = (await AppendToStreamBatch(new BatchAppendReq {
					Options = new() {
						Any = new(),
						StreamIdentifier = new() {StreamName = ByteString.CopyFromUtf8(streamName)}
					},
					IsFinal = true,
					ProposedMessages = {CreateEvents(1)},
					CorrelationId = Uuid.NewUuid().ToDto()
				})).Success;
			}
			
			_position = new Position(success.Position.CommitPosition, success.Position.PreparePosition);

			while (await call.ResponseStream.MoveNext()) {
				var response = call.ResponseStream.Current;
				if (response.ContentCase == ReadResp.ContentOneofCase.Checkpoint) {
					_positions.Add(new Position(response.Checkpoint.CommitPosition,
						response.Checkpoint.PreparePosition));
					continue;
				}

				if (response.ContentCase == ReadResp.ContentOneofCase.Event) {
					Assert.AreEqual(streamName,
						response.Event.Event.StreamIdentifier.StreamName.ToStringUtf8());
					if (!(response.Event.CommitPosition < _position.CommitPosition)) {
						return;
					}
				}
			}
		}

		[Test]
		public void receives_the_correct_number_of_checkpoints() {
			Assert.AreEqual(1, CheckpointCount);
		}

		[Test]
		public void no_duplicate_checkpoints_received() {
			Assert.AreEqual(_positions.Distinct().Count(), _positions.Count);
		}

		[Test]
		public void checkpoint_is_before_last_written_event() {
			Assert.True(_positions[0] < _position);
		}
	}
	
	[TestFixture(typeof(LogFormat.V2), typeof(string), 8, 6)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), 32, 6)]
	[TestFixture(typeof(LogFormat.V2), typeof(string), 36, 6)]
	public class when_subscribing_to_all_with_a_filter_and_transitioning_to_live<TLogFormat, TStreamId>
		: GrpcSpecification<TLogFormat, TStreamId> {

		private const string StreamA = nameof(StreamA);
		private const string MarkerStream = nameof(MarkerStream);
		private const string FinishEventType = nameof(FinishEventType);
		private const int CheckpointIntervalMultiplier = 2;
		private const int CheckpointInterval = CheckpointIntervalMultiplier * 32; 

		private int _expectedEventCount;
		private AllStreamPosition _markerPosition;
		private readonly int _numberOfEventsToCatchUp;
		private readonly int _expectedCheckpoints;
		private readonly List<int> _checkpointPositions = new (); 
		private readonly Dictionary<ReadResp.ContentOneofCase, int> _contentCaseCounts = new ();

		public when_subscribing_to_all_with_a_filter_and_transitioning_to_live(int catchupCount, int expectedCheckpoints)
			: base (new LotsOfExpiriesStrategy()) {
			
			_expectedCheckpoints = expectedCheckpoints;
			_numberOfEventsToCatchUp = catchupCount;

			foreach (var c in Enum.GetValues<ReadResp.ContentOneofCase>()) {
				_contentCaseCounts.Add(c, 0);
			}
		}

		protected override async Task Given() {
			// marker stream, start subscription after this
			var result = await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(MarkerStream) }
				},
				IsFinal = true,
				ProposedMessages = { CreateEvents(1) },
				CorrelationId = Uuid.NewUuid().ToDto()
			});

			_markerPosition = result.Success.Position;
			
			// initial events used for catching-up the subscription
			await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamA) }
				},
				IsFinal = true,
				ProposedMessages = { ExpectEvents(CreateEvents(_numberOfEventsToCatchUp)) },
				CorrelationId = Uuid.NewUuid().ToDto()
			});
		}

		protected override async Task When() {
			var caughtUp = new TaskCompletionSource();
			var cancelSubscription = new TaskCompletionSource();
			var _ = Task.Run(async () => {
				// subscribe
				using var call = StreamsClient.Read(new ReadReq {
					Options = new ReadReq.Types.Options {
						Subscription = new(),
						All = new() { Position = new ReadReq.Types.Options.Types.Position() {
							CommitPosition = _markerPosition.CommitPosition,
							PreparePosition = _markerPosition.PreparePosition
						}},
						Filter = new() {
							Count = new Empty(),
							CheckpointIntervalMultiplier = CheckpointIntervalMultiplier,
							EventType = new ReadReq.Types.Options.Types.FilterOptions.Types.Expression() {
								Regex = "^[^$].*" // exclude system events
							},
						},
						UuidOption = new() { Structured = new() },
						ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards
					}
				}, GetCallOptions(AdminCredentials));
				
				// consume
				var cts = new CancellationTokenSource();
				var sw = Stopwatch.StartNew();
				var maxResponseTimeMs = TimeSpan.Zero.TotalMilliseconds;
				try {
					await foreach (var response in call.ResponseStream.ReadAllAsync(cts.Token)) {
						maxResponseTimeMs = Math.Max(maxResponseTimeMs, sw.Elapsed.TotalMilliseconds);

						_contentCaseCounts[response.ContentCase]++;

						switch (response.ContentCase) {
							case ReadResp.ContentOneofCase.Checkpoint:
								_checkpointPositions.Add(_contentCaseCounts[ReadResp.ContentOneofCase.Event]);
								break;
							case ReadResp.ContentOneofCase.CaughtUp:
								caughtUp.TrySetResult();
								break;
							case ReadResp.ContentOneofCase.Event: {
								if (response.Event.Event.Metadata[GrpcMetadata.Type] == FinishEventType) {
									// allow some time for final events, like checkpoints, to arrive
									cts.CancelAfter(TimeSpan.FromMilliseconds(maxResponseTimeMs * 10));
								}
								break;
							}
						}

						sw.Restart();
					}
				} catch (RpcException ex) when(ex.StatusCode == StatusCode.Cancelled) {
					// expected
				}
				
				cancelSubscription.TrySetResult();
			});

			// wait for initial events to be caught-up and subscription transitions to live
			await caughtUp.Task;
			
			for (int i = 0; i < 18; i++) {
				await AppendToStreamBatch(new BatchAppendReq {
					Options = new() {
						Any = new(),
						StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamA) }
					},
					IsFinal = true,
					ProposedMessages = { ExpectEvents(CreateEvents(20)) },
					CorrelationId = Uuid.NewUuid().ToDto()
				});

				await Task.Delay(10);
			}

			await AppendToStreamBatch(new BatchAppendReq {
				Options = new() {
					Any = new(),
					StreamIdentifier = new() { StreamName = ByteString.CopyFromUtf8(StreamA) }
				},
				IsFinal = true,
				ProposedMessages = {
					ExpectEvents(CreateEvents(15)),
					ExpectEvents(CreateEvent(FinishEventType))
				},
				CorrelationId = Uuid.NewUuid().ToDto()
			});
			
			await cancelSubscription.Task;
		}

		private IEnumerable<BatchAppendReq.Types.ProposedMessage> ExpectEvents(
			IEnumerable<BatchAppendReq.Types.ProposedMessage> events) => ExpectEvents(events.ToArray());

		private IEnumerable<BatchAppendReq.Types.ProposedMessage> ExpectEvents(params BatchAppendReq.Types.ProposedMessage[] events) {
			_expectedEventCount += events.Length;
			return events;
		}
		
		[Test]
		public void receives_the_correct_number_of_confirmations() {
			Assert.AreEqual(1, _contentCaseCounts[ReadResp.ContentOneofCase.Confirmation]);
		}
		
		[Test]
		public void receives_the_correct_number_of_events() {
			Assert.AreEqual(_expectedEventCount, _contentCaseCounts[ReadResp.ContentOneofCase.Event]);
		}
		
		[Test]
		public void receives_the_correct_number_of_checkpoints() {
			Assert.AreEqual(_expectedCheckpoints, _contentCaseCounts[ReadResp.ContentOneofCase.Checkpoint]);
		}
		
		[Test]
		public void receives_the_checkpoints_on_correct_interval() {
			// ideally the checkpoints should be issued after exactly every `CheckpointInterval` events.
			// this is how things worked previously but the way the enumerator works has changed for more efficiency:
			// the enumerator now starts a _permanent_ live subscription to the subscriptions service before catch up starts.
			// this causes the checkpoints received from the subscriptions service to be offset.
			// in this test it turns out that it's offset by exactly the number of events to catch up but this is not a strict requirement -
			// the important thing being verified here is that the checkpoints are received after the correct interval when live.

			var factor = 0L;
			_checkpointPositions.ForEach(p => Assert.AreEqual(factor++ * CheckpointInterval + _numberOfEventsToCatchUp, p, $"checkpoint at: {p}"));
		}
		
		[Test]
		public void receives_the_subscription_was_caught_up() {
			Assert.AreEqual(1, _contentCaseCounts[ReadResp.ContentOneofCase.CaughtUp]);
		}
		
		[Test]
		public void does_not_receive_the_subscription_fell_behind() {
			Assert.Zero(_contentCaseCounts[ReadResp.ContentOneofCase.FellBehind]);
		}
	}
}
