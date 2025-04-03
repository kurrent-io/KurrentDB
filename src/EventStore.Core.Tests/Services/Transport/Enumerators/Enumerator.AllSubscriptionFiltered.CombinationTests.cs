// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;
using EventStore.ClientAPI.Messages;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.Transport.Enumerators;
using EventStore.Core.Services.UserManagement;
using NUnit.Framework;
using ExpectedVersion = EventStore.ClientAPI.ExpectedVersion;
using Position = EventStore.Core.Services.Transport.Common.Position;
using ResolvedEvent = EventStore.ClientAPI.ResolvedEvent;

namespace EventStore.Core.Tests.Services.Transport.Enumerators;

[TestFixture]
public partial class EnumeratorTests {
	[TestFixtureSource(nameof(TestCases))]
	public class AllSubscriptionFilteredCombinationTests : TestFixtureWithMiniNodeConnection {
		public record struct StreamProperties(int NumEvents = 0);
		public record struct SubscriptionProperties(CheckpointType CheckpointType = CheckpointType.Start, EventFilterType EventFilterType = EventFilterType.None);
		public record struct LiveProperties(int NumEventsToAdd = 0, bool RevokeAccessWithStreamAcl = false, bool RevokeAccessWithDefaultAcl = false, bool FallBehindThenCatchUp = false);
		public readonly record struct TestData(string TestCase, StreamProperties StreamProperties, SubscriptionProperties SubscriptionProperties, LiveProperties LiveProperties) {
			public override string ToString() {
				return TestCase;
			}
		}
		public static object[] CreateTestData(string testCase, StreamProperties streamProperties, SubscriptionProperties subscriptionProperties, LiveProperties liveProperties) {
			return new object[] {
				new TestData(testCase, streamProperties, subscriptionProperties, liveProperties)
			};
		}

		public enum CheckpointType {
			Start = 0,
			End = 1,
			AtZero = 2,
			OneBeforeLast = 3,
			AtLast = 4,
			OneByteAfterLast = 5,
			InvalidPosition = 6
		}

		public enum EventFilterType {
			None = 0,
			StreamPrefix = 1,
			StreamRegex = 2,
			EventTypePrefix = 3,
			EventTypeRegex = 4,
		}

		private const int NumEventsToFallBehind = 3 * 32;
		private const int CheckpointInterval = 16;

		public static object[] TestCases = {
			// STREAM PREFIX FILTER
			CreateTestData(
				"subscribe to $all with stream prefix filter from start",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter from end",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.StreamPrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter at position zero",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtZero, EventFilterType.StreamPrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter at one before last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneBeforeLast, EventFilterType.StreamPrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter at last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtLast, EventFilterType.StreamPrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter at one byte after last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneByteAfterLast, EventFilterType.StreamPrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter at an invalid position",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.InvalidPosition, EventFilterType.StreamPrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter from start then revoke access with stream acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter from start then revoke access with default acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to $all with stream prefix filter from start then fall behind and catch up",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// STREAM REGEX FILTER
			CreateTestData(
				"subscribe to $all with stream regex filter from start",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter from end",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.StreamRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter at position zero",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtZero, EventFilterType.StreamRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter at one before last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneBeforeLast, EventFilterType.StreamRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter at last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtLast, EventFilterType.StreamRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter at one byte after last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneByteAfterLast, EventFilterType.StreamRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter at an invalid position",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.InvalidPosition, EventFilterType.StreamRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter from start then revoke access with stream acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamRegex),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter from start then revoke access with default acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamRegex),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to $all with stream regex filter from start then fall behind and catch up",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamRegex),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// EVENT TYPE PREFIX FILTER
			CreateTestData(
				"subscribe to $all with event type prefix filter from start",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypePrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter from end",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.EventTypePrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter at position zero",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtZero, EventFilterType.EventTypePrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter at one before last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneBeforeLast, EventFilterType.EventTypePrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter at last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtLast, EventFilterType.EventTypePrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter at one byte after last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneByteAfterLast, EventFilterType.EventTypePrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter at an invalid position",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.InvalidPosition, EventFilterType.EventTypePrefix),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter from start then revoke access with stream acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypePrefix),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter from start then revoke access with default acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypePrefix),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to $all with event type prefix filter from start then fall behind and catch up",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypePrefix),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// EVENT TYPE REGEX FILTER
			CreateTestData(
				"subscribe to $all with event type regex filter from start",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypeRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter from end",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.EventTypeRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter at position zero",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtZero, EventFilterType.EventTypeRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter at one before last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneBeforeLast, EventFilterType.EventTypeRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter at last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.AtLast, EventFilterType.EventTypeRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter at one byte after last event",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.OneByteAfterLast, EventFilterType.EventTypeRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter at an invalid position",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.InvalidPosition, EventFilterType.EventTypeRegex),
				new LiveProperties(3)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter from start then revoke access with stream acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypeRegex),
				new LiveProperties(RevokeAccessWithStreamAcl: true)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter from start then revoke access with default acl",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypeRegex),
				new LiveProperties(RevokeAccessWithDefaultAcl: true)
			),
			CreateTestData(
				"subscribe to $all with event type regex filter from start then fall behind and catch up",
				new StreamProperties(10),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.EventTypeRegex),
				new LiveProperties(FallBehindThenCatchUp: true)
			),
			// CHECKPOINTS DURING CATCH-UP
			CreateTestData(
				"subscription to filtered $all receives checkpoints during catch-up (1)",
				new StreamProperties(0),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties()
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints during catch-up (2)",
				new StreamProperties(CheckpointInterval),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties()
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints during catch-up (3)",
				new StreamProperties(CheckpointInterval + 1),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties()
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints during catch-up (4)",
				new StreamProperties(CheckpointInterval * 2),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties()
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints during catch-up (5)",
				new StreamProperties(CheckpointInterval * 2 + 1),
				new SubscriptionProperties(CheckpointType.Start, EventFilterType.StreamPrefix),
				new LiveProperties()
			),
			// CHECKPOINTS WHEN LIVE
			CreateTestData(
				"subscription to filtered $all receives checkpoints when live (1)",
				new StreamProperties(),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.StreamPrefix),
				new LiveProperties(0)
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints when live (2)",
				new StreamProperties(),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.StreamPrefix),
				new LiveProperties(CheckpointInterval)
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints when live (3)",
				new StreamProperties(),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.StreamPrefix),
				new LiveProperties(CheckpointInterval + 1)
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints when live (4)",
				new StreamProperties(),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.StreamPrefix),
				new LiveProperties(CheckpointInterval * 2)
			),
			CreateTestData(
				"subscription to filtered $all receives checkpoints when live (5)",
				new StreamProperties(),
				new SubscriptionProperties(CheckpointType.End, EventFilterType.StreamPrefix),
				new LiveProperties(CheckpointInterval * 2 + 1)
			),
		};

		private readonly TestData _testData;
		private StreamProperties StreamProps => _testData.StreamProperties;
		private SubscriptionProperties SubscriptionProps => _testData.SubscriptionProperties;
		private LiveProperties LiveProps => _testData.LiveProperties;

		private readonly Guid _testGuid;
		private readonly string _streamPrefix;
		private int _streamSuffix;
		private List<ResolvedEvent> _events = new();

		private int _nextEventIndex;

		public AllSubscriptionFilteredCombinationTests(TestData testData) {
			_testData = testData;
			_testGuid = Guid.NewGuid();
			_streamPrefix = $"stream-{_testGuid}-";
		}

		private Position GetPosition(ResolvedEvent @event) {
			var pos = @event.OriginalPosition!.Value;
			return Position.FromInt64(pos.CommitPosition, pos.PreparePosition);
		}

		private Position GetPositionPlusOneByte(ResolvedEvent @event) {
			var pos = GetPosition(@event);
			return new Position(pos.CommitPosition + 1, pos.PreparePosition + 1);
		}

		private Position GetPositionMinusOneByte(ResolvedEvent @event) {
			var pos = GetPosition(@event);
			return new Position(pos.CommitPosition - 1, pos.PreparePosition - 1);
		}

		private async Task WriteExistingEvents() {
			var numEvents = StreamProps.NumEvents;
			for (int i = 0; i < numEvents; i++)
				await WriteEvent();
		}

		private async Task PopulateExistingEvents() {
			_events.Clear();

			var filter = SubscriptionProps.EventFilterType switch {
				EventFilterType.None => null,
				EventFilterType.StreamPrefix => new Filter(ClientMessage.Filter.FilterContext.StreamId, ClientMessage.Filter.FilterType.Prefix, new [] {$"stream-{_testGuid}"}),
				EventFilterType.StreamRegex => new Filter(ClientMessage.Filter.FilterContext.StreamId, ClientMessage.Filter.FilterType.Regex, new [] {$"(.*?){_testGuid}(.*?)"}),
				EventFilterType.EventTypePrefix => new Filter(ClientMessage.Filter.FilterContext.EventType, ClientMessage.Filter.FilterType.Prefix, new [] { $"type-{_testGuid}"}),
				EventFilterType.EventTypeRegex => new Filter(ClientMessage.Filter.FilterContext.EventType, ClientMessage.Filter.FilterType.Regex, new [] { $"(.*?){_testGuid}(.*?)"}),
				_ => throw new ArgumentOutOfRangeException()
			};

			var result = await NodeConnection.FilteredReadAllEventsForwardAsync(
				position: EventStore.ClientAPI.Position.Start,
				maxCount: 1000,
				resolveLinkTos: false,
				filter: filter);

			foreach (var @event in result.Events)
				_events.Add(@event);
		}

		private async Task WriteEvent(string stream, string eventType, string data, string metadata) {
			data ??= string.Empty;
			metadata ??= string.Empty;
			var eventData = new EventData(Guid.NewGuid(), eventType, true, Encoding.UTF8.GetBytes(data), Encoding.UTF8.GetBytes(metadata));
			await NodeConnection.AppendToStreamAsync(stream, ExpectedVersion.Any, eventData);
		}
		private Task WriteEvent() => WriteEvent(_streamPrefix + _streamSuffix++ + "-filtered", $"type-{_testGuid}-filtered", "{}", null);
		private Task RevokeAccessWithStreamAcl() => WriteEvent(SystemStreams.MetastreamOf(Core.Services.SystemStreams.AllStream), "$metadata", @"{ ""$acl"": { ""$r"": [] } }", null);
		private Task RevokeAccessWithDefaultAcl() => WriteEvent(SystemStreams.SettingsStream, "update-default-acl", @"{ ""$systemStreamAcl"" : { ""$r"" : [] } }", null);

		private async Task<(int numEventsAdded, bool accessRevoked, bool fallBehindThenCatchup)> ApplyLiveProperties() {
			var numEventsAdded = 0;
			var accessRevoked = false;
			var shouldFallBehindThenCatchup = false;

			if (LiveProps.NumEventsToAdd > 0) {
				numEventsAdded = LiveProps.NumEventsToAdd;
				for (var i = 0; i < numEventsAdded; i++)
					await WriteEvent();
			} else if (LiveProps.RevokeAccessWithStreamAcl) {
				numEventsAdded = 0; // the added event does not match the filter
				accessRevoked = true;
				await RevokeAccessWithStreamAcl();
			} else if (LiveProps.RevokeAccessWithDefaultAcl) {
				numEventsAdded = 0; // the added event does not match the filter
				accessRevoked = true;
				await RevokeAccessWithDefaultAcl();
			} else if (LiveProps.FallBehindThenCatchUp) {
				numEventsAdded = NumEventsToFallBehind;
				for (var i = 0; i < NumEventsToFallBehind; i++)
					await WriteEvent();

				shouldFallBehindThenCatchup = true;
			}

			// repopulate existing events
			await PopulateExistingEvents();

			return (numEventsAdded, accessRevoked, shouldFallBehindThenCatchup);
		}

		private int CalculateNextEventIndexFromCheckpoint() {
			return SubscriptionProps.CheckpointType switch {
				CheckpointType.Start => 0,
				CheckpointType.End => _events.Count,
				CheckpointType.AtZero => 0,
				CheckpointType.OneBeforeLast => _events.Count - 1,
				CheckpointType.AtLast => _events.Count,
				CheckpointType.OneByteAfterLast => _events.Count,
				CheckpointType.InvalidPosition => -1, // unused value as an exception should be thrown
				_ => throw new ArgumentOutOfRangeException()
			};
		}

		private EnumeratorWrapper Subscribe() {
			Position? checkpoint = SubscriptionProps.CheckpointType switch {
				CheckpointType.Start => null,
				CheckpointType.End => Position.End,
				CheckpointType.AtZero => Position.Start,
				CheckpointType.OneBeforeLast => GetPosition(_events[^2]),
				CheckpointType.AtLast => GetPosition(_events[^1]),
				CheckpointType.OneByteAfterLast => GetPositionPlusOneByte(_events[^1]),
				CheckpointType.InvalidPosition => GetPositionMinusOneByte(_events[^1]),
				_ => throw new ArgumentOutOfRangeException()
			};

			IEventFilter eventFilter = SubscriptionProps.EventFilterType switch {
				EventFilterType.None => throw new ArgumentException(),
				EventFilterType.StreamPrefix => EventFilter.StreamName.Prefixes(true, $"stream-{_testGuid}"),
				EventFilterType.StreamRegex => EventFilter.StreamName.Regex(true, $"(.*?){_testGuid}(.*?)"),
				EventFilterType.EventTypePrefix => EventFilter.EventType.Prefixes(true, $"type-{_testGuid}"),
				EventFilterType.EventTypeRegex => EventFilter.EventType.Regex(true, $"(.*?){_testGuid}(.*?)"),
				_ => throw new ArgumentOutOfRangeException()
			};

			return CreateAllSubscriptionFiltered(
				publisher: Node.Node.MainQueue,
				checkpoint: checkpoint,
				eventFilter: eventFilter,
				maxSearchWindow: CheckpointInterval,
				checkpointIntervalMultiplier: 1,
				user: SystemAccounts.Anonymous);
		}

		private async Task<int> ReadExpectedEvents(EnumeratorWrapper sub, int nextEventIndex, int lastEventIndex, bool catchingUp, bool shouldFallBehindThenCatchUp = false) {
			var fellBehind = false;
			var caughtUp = false;
			var lastEventOrCheckpointPos = new TFPos(-1, -1);
			var numEventsSinceLastCheckpoint = 0;

			var numResponsesExpected = lastEventIndex - nextEventIndex + 1;
			if (shouldFallBehindThenCatchUp)
				numResponsesExpected += 2;

			while (--numResponsesExpected >= 0) {
				var response = await sub.GetNext();
				switch (response) {
					case Event evt:
						var evtPos = _events[nextEventIndex++].OriginalPosition!.Value;
						var evtTfPos = new TFPos(evtPos.CommitPosition, evtPos.PreparePosition);
						lastEventOrCheckpointPos = evtTfPos;
						numEventsSinceLastCheckpoint++;
						Assert.AreEqual(evtTfPos, evt.EventPosition!.Value);
						break;
					case FellBehind:
						if (!shouldFallBehindThenCatchUp)
							Assert.Fail("Subscription fell behind.");

						fellBehind = true;
						break;
					case CaughtUp:
						if (!fellBehind)
							Assert.Fail("Subscription caught up before falling behind");

						if (!shouldFallBehindThenCatchUp)
							Assert.Fail("Subscription fell behind then caught up.");

						caughtUp = true;
						break;
					case Checkpoint checkpoint:
						// we don't count checkpoints as part of the number of expected responses as it's
						// not always straightforward to calculate how many checkpoints we will receive.
						numResponsesExpected++;

						Assert.True(checkpoint.CheckpointPosition < Position.End);
						var checkpointPos = checkpoint.CheckpointPosition.ToInt64();
						var checkpointTfPos = new TFPos(checkpointPos.commitPosition, checkpointPos.preparePosition);

						Assert.True(checkpointTfPos >= lastEventOrCheckpointPos);
						Assert.LessOrEqual(numEventsSinceLastCheckpoint, CheckpointInterval);

						lastEventOrCheckpointPos = checkpointTfPos;
						numEventsSinceLastCheckpoint = 0;
						break;
					default:
						Assert.Fail($"Unexpected response: {response}");
						break ;
				}
			}

			bool shouldCheckpoint = false;

			// if we've just finished catching up
			shouldCheckpoint |= catchingUp && SubscriptionProps.CheckpointType is
				CheckpointType.Start or
				CheckpointType.AtZero or
				CheckpointType.OneBeforeLast;

			// or we were live and we've received `checkpoint interval` events since the last checkpoint
			shouldCheckpoint |= !catchingUp && numEventsSinceLastCheckpoint == CheckpointInterval;

			// then expect a final checkpoint
			if (shouldCheckpoint) {
				var checkpoint = AssertEx.IsType<Checkpoint>(await sub.GetNext());
				Assert.True(checkpoint.CheckpointPosition < Position.End);
				numEventsSinceLastCheckpoint = 0;
			}

			Assert.Less(numEventsSinceLastCheckpoint, CheckpointInterval);

			if (shouldFallBehindThenCatchUp) {
				if (!fellBehind)
					Assert.Fail("Subscription did not fall behind.");

				if (!caughtUp)
					Assert.Fail("Subscription fell behind but did not catch up.");
			}

			return nextEventIndex;
		}

		[SetUp]
		public async Task SetUp() {
			await WriteExistingEvents();
			await PopulateExistingEvents();
			_nextEventIndex = CalculateNextEventIndexFromCheckpoint();
		}

		[Test]
		public async Task enumeration_is_correct() {
			var sub = Subscribe();

			Assert.True(await sub.GetNext() is SubscriptionConfirmation);

			if (SubscriptionProps.CheckpointType == CheckpointType.InvalidPosition) {
				Assert.ThrowsAsync<ReadResponseException.InvalidPosition>(async () => await sub.GetNext());
				return;
			}

			_nextEventIndex = await ReadExpectedEvents(sub, _nextEventIndex, _events.Count - 1, catchingUp: true);

			Assert.True(await sub.GetNext() is CaughtUp);

			var (numEventsAdded, accessRevoked, shouldFallBehindThenCatchup) = await ApplyLiveProperties();

			_nextEventIndex = await ReadExpectedEvents(sub, _nextEventIndex, _nextEventIndex + numEventsAdded - 1, catchingUp: false, shouldFallBehindThenCatchup);

			if (accessRevoked) {
				Assert.ThrowsAsync<ReadResponseException.AccessDenied>(async () => await sub.GetNext());
			}
		}
	}
}
