// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

[TestFixture]
public class PersistentSubscriptionMessageParkerTests {
	private static string LinkMetadata =
		"{\"added\":\"2021-01-19T11:40:46.2592636+01:00\",\"reason\":\"Client explicitly NAK'ed message.\"}";

	private static ResolvedEvent CreateResolvedEvent(long eventNumber, long logPosition) {
		var record = new EventRecord(eventNumber, logPosition, Guid.NewGuid(), Guid.NewGuid(), 0,
			0, "foo", ExpectedVersion.Any, DateTime.Now, PrepareFlags.IsCommitted, "test-event",
			Encoding.UTF8.GetBytes("{\"foo\": \"bar\"}"),
			new byte[] { });
		return ResolvedEvent.ForResolvedLink(record, null, 0);
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_parked_stream_does_not_exist<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		protected override void Given() {
			base.Given();
			NoOtherStreams();
			_messageParker = new PersistentSubscriptionMessageParker(Guid.NewGuid().ToString(), _ioDispatcher);
		}

		[Test]
		public async Task should_have_no_parked_messages() {
			_messageParker.BeginLoadStats(() => {
				Assert.Zero(_messageParker.ParkedMessageCount);
				Assert.Null(_messageParker.GetOldestParkedMessage);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_parked_messages_and_no_truncate_before<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		protected override void Given() {
			base.Given();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "0@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "1@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "2@foo");
			NoOtherStreams();
		}

		[Test]
		public async Task should_have_three_parked_messages() {
			_messageParker.BeginLoadStats(() => {
				Assert.AreEqual(3, _messageParker.ParkedMessageCount);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_parked_messages_and_no_truncate_before_oldest_parked_message_timestamp<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();
		private List<EventRecord> _eventRecords = new List<EventRecord>();

		protected override void Given() {
			base.Given();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "0@foo"));
			_timeProvider.AddToUtcTime(new TimeSpan(0,0,1,2));
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "1@foo"));
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "2@foo"));
			NoOtherStreams();
		}

		[Test]
		public async Task should_have_the_first_parked_message_timestamp_as_oldest_parked_message_timestamp() {
			_messageParker.BeginLoadStats(() => {
				Assert.AreEqual(_eventRecords[0].TimeStamp, _messageParker.GetOldestParkedMessage);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_parked_messages_and_half_are_truncated<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();

		protected override void Given() {
			base.Given();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "5@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "6@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "7@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "8@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "9@foo");
		}

		[Test]
		public async Task should_have_five_parked_messages() {
			_messageParker.BeginLoadStats(() => {
				Assert.AreEqual(5, _messageParker.ParkedMessageCount);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_parked_messages_and_half_are_truncated_oldest_parked_message_timestamp<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private List<EventRecord> _eventRecords = new List<EventRecord>();

		protected override void Given() {
			base.Given();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "5@foo"));
			_timeProvider.AddToUtcTime(new TimeSpan(0,0,1,2));
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "6@foo"));
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "7@foo"));
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "8@foo"));
			_eventRecords.Add(ExistingEventTimeStamp(_messageParker.ParkedStreamId, "$>", LinkMetadata, "9@foo"));
		}

		[Test]
		public async Task should_have_the_first_parked_message_timestamp_as_oldest_parked_message_timestamp() {
			_messageParker.BeginLoadStats(() => {
				Assert.AreEqual(_eventRecords[0].TimeStamp,
					_messageParker.GetOldestParkedMessage);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_parked_messages_and_all_are_truncated<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		protected override void Given() {
			base.Given();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "0@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "1@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "2@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "3@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "4@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "5@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "6@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "7@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "8@foo");
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "9@foo");
			DeletedStream(_messageParker.ParkedStreamId);
		}

		[Test]
		public async Task should_have_no_parked_messages() {
			_messageParker.BeginLoadStats(() => {
				Assert.Zero(_messageParker.ParkedMessageCount);
				Assert.Null(_messageParker.GetOldestParkedMessage);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_a_message_is_parked<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		protected override void Given() {
			base.Given();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			NoOtherStreams();
			AllWritesSucceed();
		}

		[Test]
		public async Task should_have_one_parked_message() {
			_messageParker.BeginParkMessage(CreateResolvedEvent(0, 0), "testing", (_, __) => {
				Assert.AreEqual(1, _messageParker.ParkedMessageCount);
				Assert.AreEqual(EventTimeStamps[0], _messageParker.GetOldestParkedMessage);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_messages_are_parked_and_then_replayed<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _parked;
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		protected override void Given() {
			base.Given();

			AllWritesSucceed();

			_parked = new TaskCompletionSource<bool>();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			_messageParker.BeginParkMessage(CreateResolvedEvent(0, 0), "testing", (_, __) => {
				_messageParker.BeginParkMessage(CreateResolvedEvent(1, 100), "testing", (_, __) => {
					_parked.SetResult(true);
				});
			});
		}

		[Test]
		public async Task should_have_no_parked_messages() {
			await _parked.Task;
			_messageParker.BeginMarkParkedMessagesReprocessed(2, null, true, () => {
				Assert.Zero(_messageParker.ParkedMessageCount);
				Assert.Null(_messageParker.GetOldestParkedMessage);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_message_parked_after_parked_messages_are_replayed<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _replayParked;
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		protected override void Given() {
			base.Given();

			AllWritesSucceed();

			_replayParked = new TaskCompletionSource<bool>();
			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			_messageParker.BeginParkMessage(CreateResolvedEvent(0, 0), "testing", (_, __) => {
				_messageParker.BeginParkMessage(CreateResolvedEvent(1, 100), "testing", (_, __) => {
					_messageParker.BeginMarkParkedMessagesReprocessed(2, null, true, () => {
						_replayParked.SetResult(true);
					});
				});
			});
		}

		[Test]
		public async Task should_have_one_parked_message() {
			await _replayParked.Task;
			_timeProvider.AddToUtcTime(new TimeSpan(0,0,1,0));
			_messageParker.BeginParkMessage(CreateResolvedEvent(2,200), "testing", (_, __) => {
				Assert.AreEqual(1, _messageParker.ParkedMessageCount);
				Assert.AreEqual(EventTimeStamps[2], _messageParker.GetOldestParkedMessage);
				_done.TrySetResult(true);
			});
			await _done.Task.WithTimeout();
		}
	}


	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_read_backwards_fails_when_getting_stats<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		private TaskCompletionSource<bool> _timerMessageReceived = new TaskCompletionSource<bool>();
		private IODispatcherDelayedMessage _timerMessage;

		protected override void Given() {
			base.Given();

			AllReadsTimeOut();

			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);

			_bus.Subscribe(new AdHocHandler<TimerMessage.Schedule>(
				msg => {
					_timerMessage = msg.ReplyMessage as IODispatcherDelayedMessage;
					_timerMessageReceived.TrySetResult(true);
				}));
		}

		[Test]
		public async Task should_not_hang() {
			_messageParker.BeginLoadStats(() => {
				Assert.Zero(_messageParker.ParkedMessageCount);
				_done.TrySetResult(true);
			});

			await _timerMessageReceived.Task.WithTimeout();
			_ioDispatcher.Handle(_timerMessage);

			await _done.Task.WithTimeout();
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class given_read_forwards_fails_when_getting_stats<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
		private PersistentSubscriptionMessageParker _messageParker;
		private string _streamId = Guid.NewGuid().ToString();
		private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>();

		private TaskCompletionSource<bool> _timerMessagesReceived = new TaskCompletionSource<bool>();
		private TaskCompletionSource<bool> _readForwardReceived = new TaskCompletionSource<bool>();
		private List<IODispatcherDelayedMessage> _timerMessages = new List<IODispatcherDelayedMessage>();
		private Guid _readForwardCorrelationId;

		protected override void Given() {
			base.Given();

			_messageParker = new PersistentSubscriptionMessageParker(_streamId, _ioDispatcher);
			ExistingEvent(_messageParker.ParkedStreamId, "$>", LinkMetadata, "0@foo");

			// Disable the forward reader so it times out
			_bus.Unsubscribe(_ioDispatcher.ForwardReader);
			_bus.Subscribe(new AdHocHandler<ClientMessage.ReadStreamEventsForward>(msg => {
				_readForwardCorrelationId = msg.CorrelationId;
				_readForwardReceived.TrySetResult(true);
			}));
			_bus.Subscribe(new AdHocHandler<TimerMessage.Schedule>(
				msg => {
					_timerMessages.Add(msg.ReplyMessage as IODispatcherDelayedMessage);
					if (_timerMessages.Count == 2) _timerMessagesReceived.TrySetResult(true);
				}));
		}

		[Test]
		public async Task should_not_hang() {
			_messageParker.BeginLoadStats(() => {
				Assert.AreEqual(1, _messageParker.ParkedMessageCount);
				_done.TrySetResult(true);
			});

			await _readForwardReceived.Task.WithTimeout();
			await _timerMessagesReceived.Task.WithTimeout();

			_ioDispatcher.Handle(_timerMessages.FirstOrDefault(x => x.MessageCorrelationId == _readForwardCorrelationId));

			await _done.Task.WithTimeout();
		}
	}
}
