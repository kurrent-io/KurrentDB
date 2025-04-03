// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using EventStore.ClientAPI.Common;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ExpectedVersion = EventStore.Core.Data.ExpectedVersion;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;
using EventStore.Core.Tests.ClientAPI;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messaging;
using EventStore.Core.Metrics;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.TransactionLog;
using EventFilter = EventStore.Core.Services.Storage.ReaderIndex.EventFilter;
using StreamMetadata = EventStore.Core.Data.StreamMetadata;

namespace EventStore.Core.Tests.Services.PersistentSubscription;

public enum EventSource {
	SingleStream,
	AllStream,
	FilteredAllStream
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class when_creating_persistent_subscription {
	private Core.Services.PersistentSubscription.PersistentSubscription _sub;
	private readonly EventSource _eventSource;
	private readonly string _streamName;

	public when_creating_persistent_subscription(EventSource eventSource) {
		_eventSource = eventSource;
		_streamName = _eventSource switch {
			EventSource.AllStream => "$all",
			EventSource.FilteredAllStream => "$all",
			EventSource.SingleStream => "streamName",
			_ => throw new InvalidOperationException()
		};
	}

	[OneTimeSetUp]
	public void Setup() {
		_sub = new Core.Services.PersistentSubscription.PersistentSubscription(
				Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
					.WithEventLoader(new FakeStreamReader())
					.WithCheckpointReader(new FakeCheckpointReader())
					.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
					.WithMessageParker(new FakeMessageParker()));
	}

	[Test]
	public void subscription_id_is_set() {
		Assert.AreEqual(_streamName + ":groupName", _sub.SubscriptionId);
	}

	[Test]
	public void stream_name_is_set() {
		Assert.AreEqual(_streamName, _sub.EventSource.ToString());
	}

	[Test]
	public void group_name_is_set() {
		Assert.AreEqual("groupName", _sub.GroupName);
	}

	[Test]
	public void there_are_no_clients() {
		Assert.IsFalse(_sub.HasClients);
		Assert.AreEqual(0, _sub.ClientCount);
	}

	[Test]
	public void null_checkpoint_reader_throws_argument_null() {
		Assert.Throws<ArgumentNullException>(() => {
			_sub = new Core.Services.PersistentSubscription.PersistentSubscription(
				Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
					.WithEventLoader(new FakeStreamReader())
					.WithCheckpointReader(null)
					.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
					.WithMessageParker(new FakeMessageParker()));
		});
	}

	[Test]
	public void null_checkpoint_writer_throws_argument_null() {
		Assert.Throws<ArgumentNullException>(() => {
			_sub = new Core.Services.PersistentSubscription.PersistentSubscription(
				Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
					.WithEventLoader(new FakeStreamReader())
					.WithCheckpointReader(new FakeCheckpointReader())
					.WithCheckpointWriter(null)
					.WithMessageParker(new FakeMessageParker()));
		});
	}


	[Test]
	public void null_event_reader_throws_argument_null() {
		Assert.Throws<ArgumentNullException>(() => {
			_sub = new Core.Services.PersistentSubscription.PersistentSubscription(
				Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
					.WithEventLoader(null)
					.WithCheckpointReader(new FakeCheckpointReader())
					.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
					.WithMessageParker(new FakeMessageParker()));
		});
	}

	[Test]
	public void null_stream_throws_argument_null() {
		if (_eventSource != EventSource.SingleStream) {
			return;
		}

		Assert.Throws<ArgumentNullException>(() => {
			_sub = new Core.Services.PersistentSubscription.PersistentSubscription(
				PersistentSubscriptionToStreamParamsBuilder.CreateFor(null, "groupName")
					.WithEventLoader(new FakeStreamReader())
					.WithCheckpointReader(new FakeCheckpointReader())
					.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
					.WithMessageParker(new FakeMessageParker()));
		});
	}

	[Test]
	public void null_groupname_throws_argument_null() {
		switch (_eventSource)
		{
			case EventSource.SingleStream:
				Assert.Throws<ArgumentNullException>(() => {
					_sub = new Core.Services.PersistentSubscription.PersistentSubscription(
						PersistentSubscriptionToStreamParamsBuilder.CreateFor("streamName", null)
							.WithEventLoader(new FakeStreamReader())
							.WithCheckpointReader(new FakeCheckpointReader())
							.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
							.WithMessageParker(new FakeMessageParker()));
				});
				break;
			case EventSource.AllStream:
				Assert.Throws<ArgumentNullException>(() => {
					_sub = new Core.Services.PersistentSubscription.PersistentSubscription(
						PersistentSubscriptionToAllParamsBuilder.CreateFor(null)
							.WithEventLoader(new FakeStreamReader())
							.WithCheckpointReader(new FakeCheckpointReader())
							.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
							.WithMessageParker(new FakeMessageParker()));
				});
				break;
		}
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_updating_all_stream_subscription_with_filter<TLogFormat, TStreamId> {

	private readonly PersistentSubscriptionService<TStreamId> _sut;
	private readonly FakeStorage _storage;
	private ClientMessage.CreatePersistentSubscriptionToAll _create;
	private IEventFilter _filterWhenCreate = EventFilter.EventType.Prefixes(isAllStream: true, new[] { "prefixFilter" });

	public when_updating_all_stream_subscription_with_filter() {
		var bus = new SynchronousScheduler();
		var ioDispatcher = new IODispatcher(bus, bus);
		var trackers = new Trackers();

		_storage = new FakeStorage(cfgs => cfgs.Count() >= 2);

		bus.Subscribe<ClientMessage.ReadStreamEventsBackward>(_storage);
		bus.Subscribe<ClientMessage.WriteEvents>(_storage);
		bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(ioDispatcher.BackwardReader);
		bus.Subscribe<ClientMessage.WriteEventsCompleted>(ioDispatcher.Writer);

		_sut = new PersistentSubscriptionService<TStreamId>(
			new QueuedHandlerThreadPool(bus, "test", new QueueStatsManager(), new QueueTrackers()),
			new FakeReadIndex<TLogFormat,TStreamId>(_ => false, new MetaStreamLookup()),
			ioDispatcher, bus,
			new PersistentSubscriptionConsumerStrategyRegistry(bus, bus,
				Array.Empty<IPersistentSubscriptionConsumerStrategyFactory>()), trackers.PersistentSubscriptionTracker);

		_sut.Start();
		_sut.Handle(new SystemMessage.BecomeLeader(correlationId: Guid.NewGuid()));
		_sut.Handle(CreateMessage());
	}

	[Test]
	public async Task should_not_overwrite_filter() {

		_sut.Handle(UpdateMessage(_create.GroupName, new NoopEnvelope()));

		await _storage.FinishedWriting.Task;

		var filterWhenCreateDto = EventFilter.ParseToDto(_filterWhenCreate);
		var allFiltersSet = _storage.Configurtations.All(
			cfg => cfg.Entries.All(
				e => (e.Filter is not null
					&& e.Filter.Data.Equals(filterWhenCreateDto.Data)
					&& e.Filter.IsAllStream)));

		Assert.True(allFiltersSet, "Expected all filters to be set!");
	}

	[Test]
	public async Task non_existent_replies_DoesNotExist() {
		var envelope = new TcsEnvelope<ClientMessage.UpdatePersistentSubscriptionToAllCompleted>();
		_sut.Handle(UpdateMessage("nonexistent", envelope));

		var response = await envelope.Task;

		Assert.AreEqual(
			ClientMessage.UpdatePersistentSubscriptionToAllCompleted.UpdatePersistentSubscriptionToAllResult.DoesNotExist,
			response.Result);

		Assert.AreEqual("Group 'nonexistent' does not exist.", response.Reason);
	}

	private ClientMessage.CreatePersistentSubscriptionToAll CreateMessage() {
		_create = new ClientMessage.CreatePersistentSubscriptionToAll(
			internalCorrId: Guid.NewGuid(),
			correlationId: Guid.NewGuid(),
			envelope: new NoopEnvelope(),
			groupName: "someGroup",
			eventFilter: _filterWhenCreate,
			resolveLinkTos: false,
			startFrom: new(0, 0),
			messageTimeoutMilliseconds: -1, false,
			maxRetryCount: 0,
			bufferSize: 10,
			liveBufferSize: 0,
			readbatchSize: 5,
			checkPointAfterMilliseconds: 0,
			minCheckPointCount: 0,
			maxCheckPointCount: 0,
			maxSubscriberCount: 0,
			namedConsumerStrategy: "Pinned",
			user: ClaimsPrincipal.Current);

		return _create;
	}

	private ClientMessage.UpdatePersistentSubscriptionToAll UpdateMessage(string name, IEnvelope envelope) => new(
		internalCorrId: Guid.NewGuid(),
		correlationId: Guid.NewGuid(),
		envelope: envelope,
		groupName: name,
		resolveLinkTos: _create.ResolveLinkTos,
		startFrom: _create.StartFrom,
		messageTimeoutMilliseconds: _create.MessageTimeoutMilliseconds,
		recordStatistics: _create.RecordStatistics,
		maxRetryCount: _create.MaxRetryCount,
		bufferSize: _create.BufferSize,
		liveBufferSize: _create.LiveBufferSize,
		readbatchSize: _create.ReadBatchSize,
		checkPointAfterMilliseconds: _create.CheckPointAfterMilliseconds,
		minCheckPointCount: _create.MinCheckPointCount,
		maxCheckPointCount: _create.MaxCheckPointCount,
		maxSubscriberCount: _create.MaxSubscriberCount,
		namedConsumerStrategy: _create.NamedConsumerStrategy,
		user: ClaimsPrincipal.Current);

	private class FakeStorage :
		IHandle<ClientMessage.ReadStreamEventsBackward>,
		IHandle<ClientMessage.WriteEvents> {
		private readonly Func<IEnumerable<PersistentSubscriptionConfig>, bool> _isDoneWriting;
		private readonly List<PersistentSubscriptionConfig> _configurations = new();
		private readonly Dictionary<string, List<Event>> _streams = new();
		private readonly TaskCompletionSource _tcs = new();

		public IEnumerable<PersistentSubscriptionConfig> Configurtations => _configurations;
		public TaskCompletionSource FinishedWriting => _tcs;

		public FakeStorage(Func<IEnumerable<PersistentSubscriptionConfig>, bool> isDoneWriting) {
			_isDoneWriting = isDoneWriting;
		}

		public void Handle(ClientMessage.ReadStreamEventsBackward msg) {
			Data.ReadStreamResult result = Data.ReadStreamResult.NoStream;
			List<ResolvedEvent> resolvedEvents = new();

			if (_streams.TryGetValue(msg.EventStreamId, out var events)) {
				result = Data.ReadStreamResult.Success;

				foreach (var ev in events) {
					var prepareLogRecord = new PrepareLogRecord(
						// values of most fields are not used and not important for the test
						logPosition: 0,
						correlationId: Guid.NewGuid(),
						eventId: Guid.NewGuid(),
						transactionPosition: 0,
						transactionOffset: 0,
						eventStreamId: msg.EventStreamId,
						eventStreamIdSize: null,
						expectedVersion: 0,
						timeStamp: DateTime.UtcNow,
						PrepareFlags.Data,
						eventType: ev.EventType,
						eventTypeSize: null,
						data: Array.Empty<byte>(),
						metadata: Array.Empty<byte>());

					var evRec = new EventRecord(0, prepareLogRecord, msg.EventStreamId, ev.EventType);
					resolvedEvents.Add(ResolvedEvent.ForUnresolvedEvent(evRec));
				}
			}

			msg.Envelope.ReplyWith(new ClientMessage.ReadStreamEventsBackwardCompleted(
				// values of most fields are not used and not important for the test
				correlationId: msg.CorrelationId,
				eventStreamId: msg.EventStreamId,
				fromEventNumber: 0,
				maxCount: msg.MaxCount,
				result: result,
				events: resolvedEvents.ToArray(),
				streamMetadata: new StreamMetadata(),
				isCachePublic: false,
				error: null,
				nextEventNumber: -1,
				lastEventNumber: 0,
				isEndOfStream: true,
				tfLastCommitPosition: 0));
		}

		public void Handle(ClientMessage.WriteEvents msg) {
			if (!_streams.TryGetValue(msg.EventStreamId, out var events)) {
				events = new List<Event>();
				_streams.Add(msg.EventStreamId, events);
			}

			events.AddRange(msg.Events);

			if (msg.Events.Any(ee => ee.EventType == "$PersistentConfig")) {
				var cfg = PersistentSubscriptionConfig.FromSerializedForm(msg.Events[0].Data);
				_configurations.Add(cfg);

				if (_isDoneWriting(_configurations)) {
					_tcs.SetResult();
				}
			}

			msg.Envelope.ReplyWith(new ClientMessage.WriteEventsCompleted(
				// values are not used and not important for the test
				msg.CorrelationId,
				firstEventNumber: 0,
				lastEventNumber: 0,
				preparePosition: 0,
				commitPosition: 0));
		}
	}

	private class MetaStreamLookup : IMetastreamLookup<TStreamId> {
		public bool IsMetaStream(TStreamId streamId) => throw new NotSupportedException();

		public TStreamId MetaStreamOf(TStreamId streamId) => throw new NotSupportedException();

		public TStreamId OriginalStreamOf(TStreamId streamId) => throw new NotSupportedException();
	}
}


[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class LiveTests {
	private readonly EventSource _eventSource;

	public LiveTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void live_subscription_pushes_events_to_client() {
		var envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope, 10, "foo", "bar");
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource));
		Assert.AreEqual(1, envelope.Replies.Count);
	}

	[Test]
	public void live_subscription_with_round_robin_two_pushes_events_to_both() {
		var envelope1 = new FakeEnvelope();
		var envelope2 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferRoundRobin()
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope2, 10, "foo", "bar");
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource));
		Assert.AreEqual(1, envelope1.Replies.Count);
		Assert.AreEqual(1, envelope2.Replies.Count);
	}

	[Test]
	public void live_subscription_with_prefer_one_and_two_pushes_events_to_both() {
		var envelope1 = new FakeEnvelope();
		var envelope2 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope2, 10, "foo", "bar");
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource));
		Assert.AreEqual(2, envelope1.Replies.Count);
	}


	[Test]
	public void subscription_with_pull_sends_data_to_client() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {Helper.GetFakeEventFor(0, _eventSource)}, Helper.GetStreamPositionFor(1, _eventSource), false);
		Assert.AreEqual(1, envelope1.Replies.Count);
	}

	[Test]
	public void subscription_with_pull_does_not_crash_if_not_ready_yet() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());
		Assert.DoesNotThrow(() => {
			sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
			sub.HandleReadCompleted(new[] {Helper.GetFakeEventFor( 0, _eventSource)}, Helper.GetStreamPositionFor(1, _eventSource),
				false);
		});
	}

	[Test]
	public void subscription_with_live_data_does_not_crash_if_not_ready_yet() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());
		Assert.DoesNotThrow(() => {
			sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
			sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource));
		});
	}


	[Test]
	public void subscription_with_pull_and_round_robin_set_and_two_clients_sends_data_to_client() {
		var envelope1 = new FakeEnvelope();
		var envelope2 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferRoundRobin()
				.StartFromBeginning());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope2, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		Assert.AreEqual(1, envelope1.Replies.Count);
		Assert.AreEqual(1, envelope2.Replies.Count);
	}


	[Test]
	public void subscription_with_pull_and_prefer_one_set_and_two_clients_sends_data_to_one_client() {
		var envelope1 = new FakeEnvelope();
		var envelope2 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope2, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		Assert.AreEqual(2, envelope1.Replies.Count);
	}

	[Test]
	public async Task
		when_reading_end_of_stream_and_a_live_event_is_received_subscription_should_read_stream_again() {
		var eventsFoundSource = new TaskCompletionSource<bool>();
		var envelope = new FakeEnvelope();
		var checkpointReader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader(
					(stream, startEventNumber, countToLoad, batchSize, maxWindowSize, resolveLinkTos, skipFirstEvent, onEventsFound) => {
						List<ResolvedEvent> events = new List<ResolvedEvent>();
						int nextPosition;
						bool isEndOfStream;

						if (startEventNumber.Equals(Helper.GetStreamPositionFor(0, _eventSource))) {
							//Existing events: #0, #1, #2
							if (!skipFirstEvent) {
								events.Add(Helper.GetFakeEventFor(0, _eventSource));
							}
							events.Add(Helper.GetFakeEventFor(1, _eventSource));
							events.Add(Helper.GetFakeEventFor(2, _eventSource));
							nextPosition = 3;
							isEndOfStream = true;
						} else if (startEventNumber.Equals(Helper.GetStreamPositionFor(3, _eventSource))) {
							//New live event: #3
							events.Add(Helper.GetFakeEventFor(3, _eventSource));
							nextPosition = 4;
							isEndOfStream = true;
						} else {
							throw new Exception("Invalid start event number: " + startEventNumber);
						}

						Task.Delay(100).ContinueWith(action => {
							onEventsFound(events.ToArray(), Helper.GetStreamPositionFor(nextPosition, _eventSource), isEndOfStream);
							if (startEventNumber.Equals(Helper.GetStreamPositionFor(3, _eventSource))) {
								eventsFoundSource.TrySetResult(true);
							}
						});
					}))
				.WithCheckpointReader(checkpointReader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());

		//load the existing checkpoint at event #0
		//this should trigger reading of events #0, #1 and #2 reaching the end of stream.
		//the read is handled by the subscription after 100ms
		checkpointReader.Load(Helper.GetStreamPositionFor(0, _eventSource).ToString());

		//Meanwhile, during this 100ms time window, a new live event #3 comes in and subscription is notified
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource));

		await eventsFoundSource.Task;

		//the read handled by the subscription after 100ms should trigger a second read to obtain the event #3 (which will be handled after 100ms more)

		//a subscriber coming in a while later, should receive all 3 events
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope, 10, "foo", "bar");

		//all 3 events should be received by the subscriber (except event 0 - the checkpoint which is skipped)
		Assert.AreEqual(3, envelope.Replies.Count);
	}
}

public class FilteredAllTests {
	private EventSource _eventSource = EventSource.FilteredAllStream;
	[Test]
	public void live_subscription_with_stream_prefix_filter_pushes_matching_events_to_client() {
		var envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var streamFilter = EventFilter.StreamName.Prefixes(true, "test");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", streamFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope, 10, "foo", "bar");
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, streamPrefix:"test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource, streamPrefix:"something"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(2, _eventSource, streamPrefix:"test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource, streamPrefix:"foo"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(4, _eventSource, streamPrefix:"bar"));
		Assert.AreEqual(2, envelope.Replies.Count);
	}

	[Test]
	public void live_subscription_with_stream_regex_filter_pushes_matching_events_to_client() {
		var envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var streamFilter = EventFilter.StreamName.Regex(true, "^te");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", streamFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope, 10, "foo", "bar");
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, streamPrefix:"test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource, streamPrefix:"ttest"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(2, _eventSource, streamPrefix:"team"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource, streamPrefix:"tteam"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(4, _eventSource, streamPrefix:"bar"));
		Assert.AreEqual(2, envelope.Replies.Count);
	}

	[Test]
	public void live_subscription_with_event_type_prefix_filter_pushes_matching_events_to_client() {
		var envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.EventType.Prefixes(true, "test");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope, 10, "foo", "bar");
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, eventType:"test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource, eventType:"something"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(2, _eventSource, eventType:"test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource, eventType:"foo"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(4, _eventSource, eventType:"bar"));
		Assert.AreEqual(2, envelope.Replies.Count);
	}

	[Test]
	public void live_subscription_with_event_type_regex_filter_pushes_matching_events_to_client() {
		var envelope = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.EventType.Regex(true, "^te");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromCurrent());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope, 10, "foo", "bar");
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, eventType:"test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource, eventType:"ttest"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(2, _eventSource, eventType:"team"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource, eventType:"tteam"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(4, _eventSource, eventType:"bar"));
		Assert.AreEqual(2, envelope.Replies.Count);
	}

	[Test]
	public void catching_up_subscription_does_write_checkpoint_on_time_when_min_events_have_been_skipped() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "stream");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleSkippedEvents(Helper.GetStreamPositionFor(1, _eventSource), 2);
		sub.NotifyClockTick(DateTime.UtcNow);
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void catching_up_subscription_does_write_checkpoint_when_max_events_have_been_skipped() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "stream");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleSkippedEvents(Helper.GetStreamPositionFor(1, _eventSource), 6);

		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void catching_up_subscription_does_write_checkpoint_when_max_events_seen_and_some_skipped() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "stream");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});

		sub.HandleSkippedEvents(Helper.GetStreamPositionFor(2, _eventSource), 3);
		Assert.AreEqual(Helper.GetStreamPositionFor(2, _eventSource), cp);
	}

	[Test]
	public void catching_up_subscription_does_not_write_checkpoint_when_max_events_skipped_and_some_messages_outstanding() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "stream");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);

		sub.HandleSkippedEvents(Helper.GetStreamPositionFor(2, _eventSource), 5);
		Assert.IsNull(cp);
	}

	[Test]
	public void live_subscription_does_write_checkpoint_when_max_events_skipped() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "foo");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(Array.Empty<ResolvedEvent>(), new PersistentSubscriptionAllStreamPosition(0, 0), true);

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(2, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(4, _eventSource, streamPrefix: "test"));
		Assert.AreEqual(Helper.GetStreamPositionFor(4, _eventSource), cp);
	}

	[Test]
	public void live_subscription_does_write_checkpoint_on_time_when_min_events_skipped() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "foo");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(Array.Empty<ResolvedEvent>(), new PersistentSubscriptionAllStreamPosition(0, 0), true);

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, streamPrefix: "test"));

		sub.NotifyClockTick(DateTime.UtcNow);
		Assert.AreEqual(Helper.GetStreamPositionFor(0, _eventSource), cp);
	}

	[Test]
	public void live_subscription_does_write_checkpoint_when_max_events_seen_and_some_skipped() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "foo");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(Array.Empty<ResolvedEvent>(), new PersistentSubscriptionAllStreamPosition(0, 0), true);

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, streamPrefix: "foo"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource, streamPrefix: "foo"));
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(2, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(4, _eventSource, streamPrefix: "test"));
		Assert.AreEqual(Helper.GetStreamPositionFor(4, _eventSource), cp);
	}

	[Test]
	public void live_subscription_does_not_write_checkpoint_when_max_events_skipped_and_some_messages_outstanding() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var eventFilter = EventFilter.StreamName.Prefixes(true, "foo");
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName", eventFilter)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(Array.Empty<ResolvedEvent>(), new PersistentSubscriptionAllStreamPosition(0, 0), true);

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource, streamPrefix: "foo"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource, streamPrefix: "foo"));

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(2, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(3, _eventSource, streamPrefix: "test"));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(4, _eventSource, streamPrefix: "test"));
		Assert.IsNull(cp);
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class DeleteTests {
	private readonly EventSource _eventSource;
	public DeleteTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void subscription_deletes_checkpoint_when_deleted() {
		var reader = new FakeCheckpointReader();
		var deleted = false;
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }, () => { deleted = true; }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromCurrent());
		reader.Load(null);
		sub.Delete();
		Assert.IsTrue(deleted);
	}

	[Test]
	public void subscription_deletes_parked_messages_when_deleted() {
		var reader = new FakeCheckpointReader();
		var deleted = false;
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker(() => { deleted = true; }))
				.StartFromCurrent());
		reader.Load(null);
		sub.Delete();
		Assert.IsTrue(deleted);
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class SynchronousReadingClient {
	private readonly EventSource _eventSource;
	public SynchronousReadingClient(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void subscription_with_less_than_n_events_returns_less_events_to_the_client() {
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());
		reader.Load(null);
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		var result = sub.GetNextNOrLessMessages(5).ToArray();
		Assert.AreEqual(2, result.Length);
		Assert.AreEqual(Helper.GetEventIdFor(0), result[0].ResolvedEvent.Event.EventId);
		Assert.AreEqual(Helper.GetEventIdFor(1), result[1].ResolvedEvent.Event.EventId);
	}

	[Test]
	public void subscription_with_n_events_returns_n_events_to_the_client() {
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());
		reader.Load(null);
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		var result = sub.GetNextNOrLessMessages(2).ToArray();
		Assert.AreEqual(2, result.Length);
		Assert.AreEqual(Helper.GetEventIdFor(0), result[0].ResolvedEvent.Event.EventId);
		Assert.AreEqual(Helper.GetEventIdFor(1), result[1].ResolvedEvent.Event.EventId);
	}

	[Test]
	public void subscription_with_more_than_n_events_returns_n_events_to_the_client() {
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());
		reader.Load(null);
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource),
			Helper.GetFakeEventFor(3, _eventSource),
		}, Helper.GetStreamPositionFor(4, _eventSource), false);
		var result = sub.GetNextNOrLessMessages(2).ToArray();
		Assert.AreEqual(2, result.Length);
		Assert.AreEqual(Helper.GetEventIdFor(0), result[0].ResolvedEvent.Event.EventId);
		Assert.AreEqual(Helper.GetEventIdFor(1), result[1].ResolvedEvent.Event.EventId);
	}


	[Test]
	public void subscription_with_no_events_returns_no_events_to_the_client() {
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning());
		reader.Load(null);
		var result = sub.GetNextNOrLessMessages(5);
		Assert.AreEqual(0, result.Count());
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class Checkpointing {
	private readonly EventSource _eventSource;
	public Checkpointing(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void subscription_does_not_write_checkpoint_when_max_not_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MinimumToCheckPoint(5)
				.MaximumToCheckPoint(20));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) });
		Assert.IsNull(cp);
	}

	[Test]
	public void subscription_does_not_write_checkpoint_when_min_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(20));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) });
		Assert.IsNull(cp);
	}

	[Test]
	public void subscription_does_write_checkpoint_when_max_is_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource),
		}, Helper.GetStreamPositionFor(3, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void subscription_does_not_include_outstanding_messages_when_max_is_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource),
			Helper.GetFakeEventFor(3, _eventSource),
		}, Helper.GetStreamPositionFor(4, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void subscription_can_write_checkpoint_for_event_number_0() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource),
			Helper.GetFakeEventFor(3, _eventSource),
		}, Helper.GetStreamPositionFor(4, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
		});

		Assert.AreEqual(Helper.GetStreamPositionFor(0, _eventSource), cp);
	}

	[Test]
	public void subscription_checkpoints_when_message_parked_and_max_is_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource),
			Helper.GetFakeEventFor(3, _eventSource),
		}, Helper.GetStreamPositionFor(4, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0), Helper.GetEventIdFor(2) });
		sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(1) }, NakAction.Park, "test park");
		Assert.AreEqual(Helper.GetStreamPositionFor(2, _eventSource), cp);
	}

	[Test]
	public void subscription_does_not_include_retry_messages_when_max_is_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource),
			Helper.GetFakeEventFor(3, _eventSource),
		}, Helper.GetStreamPositionFor(4, _eventSource), false);
		sub.NotAcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(2)
		}, NakAction.Retry, "test retry");
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
			Helper.GetEventIdFor(3),
		});
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void subscription_does_include_all_messages_when_no_retries_or_outstanding_and_max_is_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource)
		}, Helper.GetStreamPositionFor(3, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
			Helper.GetEventIdFor(2)
		});
		Assert.AreEqual(Helper.GetStreamPositionFor(2, _eventSource), cp);
	}

	[Test]
	public void subscription_does_write_checkpoint_on_time_when_min_is_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.PreferDispatchToSingle()
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource)
		}, Helper.GetStreamPositionFor(3, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});
		sub.NotifyClockTick(DateTime.UtcNow);
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void subscription_does_not_write_checkpoint_on_time_when_min_is_not_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MinimumToCheckPoint(2)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource)
		}, Helper.GetStreamPositionFor(3, _eventSource), false);
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});
		sub.NotifyClockTick(DateTime.UtcNow);
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void subscription_does_write_checkpoint_for_disconnected_clients_on_time_when_min_is_hit() {
		IPersistentSubscriptionStreamPosition cp = null;
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(5));
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		sub.GetNextNOrLessMessages(2).ToArray();
		sub.AcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});
		sub.NotifyClockTick(DateTime.UtcNow);
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void
		subscription_writes_correct_checkpoint_when_outstanding_messages_is_empty_and_retry_buffer_is_non_empty() {
		IPersistentSubscriptionStreamPosition cp = null;
		var reader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(new FakeMessageParker())
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(1));
		reader.Load(null);

		var clientConnectionId = Guid.NewGuid();
		var clientCorrelationId = Guid.NewGuid();
		sub.AddClient(clientCorrelationId, clientConnectionId, "connection-1", new FakeEnvelope(), 10, "foo", "bar");

		//send events 1-3, ACK event 1 only and Mark checkpoint
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource),
			Helper.GetFakeEventFor(3, _eventSource),
		}, Helper.GetStreamPositionFor(4, _eventSource), false);
		sub.GetNextNOrLessMessages(3).ToArray();
		sub.AcknowledgeMessagesProcessed(clientCorrelationId, new[] {
			Helper.GetEventIdFor(1)
		});
		sub.TryMarkCheckpoint(false);

		//checkpoint should be at event 1
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);

		//events 2 & 3 should still be in _outstandingMessages buffer
		Assert.AreEqual(sub.OutstandingMessageCount, 2);
		//retry queue should be empty
		Assert.True(sub.TryGetStreamBuffer(out var streamBuffer));
		Assert.AreEqual(streamBuffer.RetryBufferCount, 0);

		//Disconnect the client
		Assert.IsTrue(sub.RemoveClientByConnectionId(clientConnectionId));

		//this should empty the _outstandingMessages buffer and move events 2 & 3 to the retry queue
		Assert.AreEqual(sub.OutstandingMessageCount, 0);
		Assert.AreEqual(streamBuffer.RetryBufferCount, 2);

		//mark the checkpoint which should still be at event 1 although the _lastKnownMessage value is 3.
		sub.TryMarkCheckpoint(false);
		Assert.AreEqual(Helper.GetStreamPositionFor(1, _eventSource), cp);
	}

	[Test]
	public void
		subscription_ignores_replayed_events_when_checkpointing() {
		IPersistentSubscriptionStreamPosition cp = null;
		var reader = new FakeCheckpointReader();
		var messageParker = new FakeMessageParker();

		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => cp = i))
				.WithMessageParker(messageParker)
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(1));
		reader.Load(null);

		var clientConnectionId = Guid.NewGuid();
		var clientCorrelationId = Guid.NewGuid();
		sub.AddClient(clientCorrelationId, clientConnectionId, "connection-1", new FakeEnvelope(), 10, "foo", "bar");

		//handle event number 4@streamName
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(4, _eventSource)
		}, Helper.GetStreamPositionFor(5, _eventSource), true);

		//get the message and acknowledge receipt
		sub.GetNextNOrLessMessages(1).ToArray();
		sub.AcknowledgeMessagesProcessed(clientCorrelationId, new[] { Helper.GetEventIdFor(4) });

		//mark checkpoint and verify that event 4 has been checkpointed
		sub.TryMarkCheckpoint(false);
		Assert.AreEqual(Helper.GetStreamPositionFor(4, _eventSource), cp);

		//park an event (this can be done earlier too)
		var parkedEventId = Guid.NewGuid();
		var parkedEvent = Helper.BuildFakeEvent(parkedEventId, "type", "$persistentsubscription-streamName::groupName-parked", 15, 15, 15);
		messageParker.BeginParkMessage(parkedEvent, "parked", (ev, res) => { });

		//retry parked events (this sets correct _state flag so that we can call HandleParkedReadCompleted below)
		sub.RetryParkedMessages(null);

		//handle parked event 15@$persistentsubscription-streamName::groupName-parked
		//this should send the parked event to the retry buffer.
		sub.HandleParkedReadCompleted(new[] {
			parkedEvent,
		}, new PersistentSubscriptionSingleStreamPosition(16), true, 17);

		//checkpoint should still be at 4.
		sub.TryMarkCheckpoint(false);
		Assert.AreEqual(Helper.GetStreamPositionFor(4, _eventSource), cp);

		//get the message, this should send the parked event to the outstanding message cache
		sub.GetNextNOrLessMessages(1).ToArray();

		//checkpoint should still be at 4.
		sub.TryMarkCheckpoint(false);
		Assert.AreEqual(Helper.GetStreamPositionFor(4, _eventSource), cp);

		//acknowledge receipt of message. the parked event is no longer in retry or outstanding message buffers
		sub.AcknowledgeMessagesProcessed(clientCorrelationId, new[] { parkedEventId });

		//checkpoint should still be at 4.
		sub.TryMarkCheckpoint(false);
		Assert.AreEqual(Helper.GetStreamPositionFor(4, _eventSource), cp);
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class LoadCheckpointTests {
	private readonly EventSource _eventSource;
	public LoadCheckpointTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void loading_subscription_from_checkpoint_should_set_the_skip_first_event_flag() {
		bool skip = false;
		var reader = new FakeCheckpointReader();
		var streamReader = new FakeStreamReader(
			(stream, startPosition, countToLoad, batchSize, maxWindowSize, resolveLinkTos, skipFirstEvent, onEventsFound) =>
			{ skip = skipFirstEvent; }
		);
		new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(streamReader)
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		switch (_eventSource)
		{
			case EventSource.SingleStream:
				reader.Load("1");
				break;
			case EventSource.AllStream:
			case EventSource.FilteredAllStream:
				reader.Load("C:1/P:1");
				break;
		}
		Assert.IsTrue(skip);
	}

	[Test]
	public void loading_subscription_from_checkpoint_should_set_the_last_known_event_number() {
		var reader = new FakeCheckpointReader();
		var streamReader = new FakeStreamReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(streamReader)
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		string checkpoint = "";
		switch (_eventSource)
		{
			case EventSource.SingleStream:
				checkpoint = "1";
				reader.Load(checkpoint);
				break;
			case EventSource.AllStream:
			case EventSource.FilteredAllStream:
				checkpoint = "C:1/P:1";
				reader.Load(checkpoint);
				break;
		}

		var info = sub.GetStatistics();
		Assert.AreEqual(checkpoint, info.LastCheckpointedEventPosition);
	}

	[Test]
	public void loading_subscription_from_no_checkpoint_and_no_start_from_should_read_from_beginning_of_stream() {
		IPersistentSubscriptionStreamPosition actualStart = null;
		var reader = new FakeCheckpointReader();
		var streamReader = new FakeStreamReader(
			(stream, startPosition, countToLoad, batchSize, maxWindowSize, resolveLinkTos, skipFirstEvent, onEventsFound) => {
				actualStart = startPosition;
			});
		new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(streamReader)
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.MaximumToCheckPoint(1));
		reader.Load(null);

		switch (_eventSource)
		{
			case EventSource.SingleStream:
				Assert.AreEqual(new PersistentSubscriptionSingleStreamPosition(0L), actualStart);
				break;
			case EventSource.AllStream:
			case EventSource.FilteredAllStream:
				Assert.AreEqual(new PersistentSubscriptionAllStreamPosition(0L, 0L), actualStart);
				break;
		}
	}

	[Test]
	public void loading_subscription_from_no_checkpoint_and_start_from_is_set_should_read_from_start_from() {
		IPersistentSubscriptionStreamPosition actualStart = null;

		var reader = new FakeCheckpointReader();
		var streamReader = new FakeStreamReader(
			(stream, startPosition, countToLoad, batchSize, maxWindowSize, resolveLinkTos, skipFirstEvent, onEventsFound) => {
				actualStart = startPosition;
			});
		IPersistentSubscriptionStreamPosition startFrom = _eventSource switch {
			EventSource.SingleStream => new PersistentSubscriptionSingleStreamPosition(10),
			EventSource.AllStream => new PersistentSubscriptionAllStreamPosition(10, 10),
			EventSource.FilteredAllStream => new PersistentSubscriptionAllStreamPosition(10, 10),
			_ => null
		};

		new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(streamReader)
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { }))
				.WithMessageParker(new FakeMessageParker())
				.PreferDispatchToSingle()
				.StartFrom(startFrom)
				.MaximumToCheckPoint(1));
		reader.Load(null);

		Assert.AreEqual(startFrom, actualStart);
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class TimeoutTests {
	private readonly EventSource _eventSource;
	public TimeoutTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void with_no_timeouts_to_happen_no_timeouts_happen() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.WithMessageTimeoutOf(TimeSpan.FromSeconds(3)));
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotifyClockTick(DateTime.UtcNow.AddSeconds(1));
		Assert.AreEqual(0, envelope1.Replies.Count);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}

	[Test]
	public void messages_get_timed_out_and_resent_to_clients() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.WithMessageTimeoutOf(TimeSpan.FromSeconds(1)));
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 1, "foo", "bar");
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-2", envelope1, 1, "foo", "bar");
		var id1 = Guid.NewGuid();
		var id2 = Guid.NewGuid();
		sub.HandleReadCompleted(new[] {
			Helper.BuildFakeEvent(id1, "type", "streamName", 0, 1, 1),
			Helper.BuildLinkEvent(id2, "streamName", 1,
				Helper.BuildFakeEvent(Guid.NewGuid(), "type", "streamSource", 0, 0, 0), false, 2, 2)
		}, Helper.GetStreamPositionFor(1, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotifyClockTick(DateTime.UtcNow.AddSeconds(3));
		Assert.AreEqual(2, envelope1.Replies.Count);
		var msg1 = (Messages.ClientMessage.PersistentSubscriptionStreamEventAppeared)envelope1.Replies[0];
		var msg2 = (Messages.ClientMessage.PersistentSubscriptionStreamEventAppeared)envelope1.Replies[1];
		Assert.IsTrue(id1 == msg1.Event.OriginalEvent.EventId || id1 == msg2.Event.OriginalEvent.EventId);
		Assert.IsTrue(id2 == msg1.Event.OriginalEvent.EventId || id2 == msg2.Event.OriginalEvent.EventId);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}

	[Test]
	public void messages_get_timed_out_on_synchronous_reads() {
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.WithMessageTimeoutOf(TimeSpan.FromSeconds(1)));
		reader.Load(null);
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		sub.GetNextNOrLessMessages(2);
		sub.NotifyClockTick(DateTime.Now.AddSeconds(3));
		var retries = sub.GetNextNOrLessMessages(2).ToArray();
		Assert.AreEqual(Helper.GetEventIdFor(0), retries[0].ResolvedEvent.Event.EventId);
		Assert.AreEqual(Helper.GetEventIdFor(1), retries[1].ResolvedEvent.Event.EventId);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}

	[Test]
	public void messages_dont_get_retried_when_acked_on_synchronous_reads() {
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.WithMessageTimeoutOf(TimeSpan.FromSeconds(1)));
		reader.Load(null);
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		sub.GetNextNOrLessMessages(2).ToArray();
		sub.AcknowledgeMessagesProcessed(Guid.Empty, new[] {
			Helper.GetEventIdFor(0),
			Helper.GetEventIdFor(1),
		});
		sub.NotifyClockTick(DateTime.Now.AddSeconds(3));
		var retries = sub.GetNextNOrLessMessages(2).ToArray();
		Assert.AreEqual(0, retries.Length);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}

	[Test]
	public void message_gets_timed_out_and_parked_after_max_retry_count() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.WithMaxRetriesOf(0)
				.WithMessageTimeoutOf(TimeSpan.FromSeconds(1)));
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource)
		}, Helper.GetStreamPositionFor(1, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotifyClockTick(DateTime.UtcNow.AddSeconds(3));
		Assert.AreEqual(0, envelope1.Replies.Count);
		Assert.AreEqual(1, parker.ParkedEvents.Count);
		Assert.AreEqual(Helper.GetEventIdFor(0), parker.ParkedEvents[0].OriginalEvent.EventId);
	}

	[Test]
	public void multiple_messages_get_timed_out_and_parked_after_max_retry_count() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.WithMaxRetriesOf(0)
				.WithMessageTimeoutOf(TimeSpan.FromSeconds(1)));
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotifyClockTick(DateTime.UtcNow.AddSeconds(3));
		Assert.AreEqual(0, envelope1.Replies.Count);
		Assert.AreEqual(2, parker.ParkedEvents.Count);

		var id1 = Helper.GetEventIdFor(0);
		var id2 = Helper.GetEventIdFor(1);
		Assert.IsTrue(id1 == parker.ParkedEvents[0].OriginalEvent.EventId ||
					  id1 == parker.ParkedEvents[1].OriginalEvent.EventId);
		Assert.IsTrue(id2 == parker.ParkedEvents[0].OriginalEvent.EventId ||
					  id2 == parker.ParkedEvents[1].OriginalEvent.EventId);
	}

	[Test]
	public void timeout_park_correctly_tracks_the_available_client_slots() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.WithMaxRetriesOf(0)
				.WithMessageTimeoutOf(TimeSpan.Zero)
				.StartFromBeginning());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 2, "foo", "bar");

		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);

		Assert.AreEqual(2, envelope1.Replies.Count);

		// Should expire first 2 and send to park.
		sub.NotifyClockTick(DateTime.UtcNow.AddSeconds(1));
		parker.ParkMessageCompleted(0, OperationResult.Success);
		parker.ParkMessageCompleted(1, OperationResult.Success);
		Assert.AreEqual(2, parker.ParkedEvents.Count);

		// The next 2 should still be sent to client.
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);

		Assert.AreEqual(4, envelope1.Replies.Count);
	}

	[Test]
	public void disable_timeout_doesnt_timeout() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.WithMaxRetriesOf(0)
				.PreferDispatchToSingle()
				.StartFromBeginning()
				.DontTimeoutMessages());
		reader.Load(null);
		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource)
		}, Helper.GetStreamPositionFor(2, _eventSource), false);
		envelope1.Replies.Clear();
		// Default timeout is 30s
		sub.NotifyClockTick(DateTime.UtcNow.AddMinutes(1));
		Assert.AreEqual(0, envelope1.Replies.Count);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class NAKTests {
	private readonly EventSource _eventSource;
	public NAKTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void explicit_nak_with_park_parks_the_message() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.StartFromBeginning());
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");

		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource)
		}, Helper.GetStreamPositionFor(1, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotAcknowledgeMessagesProcessed(corrid, new[] {
			Helper.GetEventIdFor(0)
		}, NakAction.Park, "a reason from client.");
		Assert.AreEqual(0, envelope1.Replies.Count);
		Assert.AreEqual(1, parker.ParkedEvents.Count);
		Assert.AreEqual(Helper.GetEventIdFor(0), parker.ParkedEvents[0].OriginalEvent.EventId);
	}

	[Test]
	public void explicit_nak_with_skip_skips_the_message() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.StartFromBeginning());
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource)
		}, Helper.GetStreamPositionFor(1, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) }, NakAction.Skip, "a reason from client.");
		Assert.AreEqual(0, envelope1.Replies.Count);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}

	[Test]
	public void explicit_nak_with_default_retries_the_message() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.StartFromBeginning());
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource)
		}, Helper.GetStreamPositionFor(1, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) }, NakAction.Unknown, "a reason from client.");
		Assert.AreEqual(1, envelope1.Replies.Count);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}

	[Test]
	public void explicit_nak_with_retry_retries_the_message() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.StartFromBeginning());
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource)
		}, Helper.GetStreamPositionFor(1, _eventSource), false);
		envelope1.Replies.Clear();
		sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) }, NakAction.Retry, "a reason from client.");
		Assert.AreEqual(1, envelope1.Replies.Count);
		Assert.AreEqual(Helper.GetEventIdFor(0),
			((ClientMessage.PersistentSubscriptionStreamEventAppeared)envelope1.Replies[0]).Event.Event.EventId);
		Assert.AreEqual(0, parker.ParkedEvents.Count);
	}

	[Test]
	public void explicit_nak_with_retry_correctly_tracks_the_available_client_slots() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMaxRetriesOf(10)
				.WithMessageParker(parker)
				.StartFromBeginning());
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 10, "foo", "bar");
		var ev = Helper.GetFakeEventFor(0, _eventSource);
		sub.HandleReadCompleted(new[] {
			ev,
		},Helper.GetStreamPositionFor(1, _eventSource), false);

		for (int i = 1; i < 11; i++) {
			sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) }, NakAction.Retry, "a reason from client.");
			Assert.AreEqual(i + 1, envelope1.Replies.Count);
		}

		Assert.That(parker.ParkedEvents, Has.No.Member(ev));

		//This time should be parked
		sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) }, NakAction.Retry, "a reason from client.");
		Assert.AreEqual(11, envelope1.Replies.Count);
		Assert.That(parker.ParkedEvents, Has.Member(ev));
	}

	[Test]
	public void explicit_nak_with_park_correctly_tracks_the_available_client_slots() {
		var envelope1 = new FakeEnvelope();
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.WithMaxRetriesOf(0)
				.WithMessageTimeoutOf(TimeSpan.Zero)
				.StartFromBeginning());
		reader.Load(null);
		var corrid = Guid.NewGuid();
		sub.AddClient(corrid, Guid.NewGuid(), "connection-1", envelope1, 1, "foo", "bar");

		sub.HandleReadCompleted(new[] {
			Helper.GetFakeEventFor(0, _eventSource),
			Helper.GetFakeEventFor(1, _eventSource),
			Helper.GetFakeEventFor(2, _eventSource)
		}, Helper.GetStreamPositionFor(3, _eventSource), false);

		Assert.AreEqual(1, envelope1.Replies.Count);

		sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(0) }, NakAction.Park, "a reason from client.");
		Assert.AreEqual(2, envelope1.Replies.Count);
		Assert.That(parker.ParkedEvents, Has.Exactly(1).Matches<ResolvedEvent>(_ => _.Event.EventId == Helper.GetEventIdFor(0) ));

		sub.NotAcknowledgeMessagesProcessed(corrid, new[] { Helper.GetEventIdFor(1) }, NakAction.Park, "a reason from client.");
		Assert.That(parker.ParkedEvents, Has.Exactly(1).Matches<ResolvedEvent>(_ => _.Event.EventId == Helper.GetEventIdFor(1)));
		Assert.AreEqual(3, envelope1.Replies.Count);
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class AddingClientTests {
	private readonly EventSource _eventSource;
	public AddingClientTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void adding_a_client_adds_the_client() {
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(new FakeCheckpointReader())
				.WithMessageParker(new FakeMessageParker())
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { })));

		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", new FakeEnvelope(), 1, "foo", "bar");
		Assert.IsTrue(sub.HasClients);
		Assert.AreEqual(1, sub.ClientCount);
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class RemoveClientTests {
	private readonly EventSource _eventSource;
	public RemoveClientTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void unsubscribing_a_client_retries_inflight_messages_immediately() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();

		var fakeCheckpointReader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(fakeCheckpointReader)
				.WithMessageParker(new FakeMessageParker())
				.PreferRoundRobin()
				.StartFromCurrent()
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { })));

		fakeCheckpointReader.Load(null);

		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		var client2Id = Guid.NewGuid();
		sub.AddClient(client2Id, Guid.NewGuid(), "connection-2", client2Envelope, 10, "foo", "bar");


		Assert.IsTrue(sub.HasClients);
		Assert.AreEqual(2, sub.ClientCount);

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		sub.RemoveClientByCorrelationId(client2Id, false);
		Assert.AreEqual(1, sub.ClientCount);

		// Message 2 should be retried on client 1 as it wasn't acked.
		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		// Retry count should have increased
		Assert.AreEqual(1,
			((ClientMessage.PersistentSubscriptionStreamEventAppeared)client1Envelope.Replies.Last()).RetryCount);
	}

	[Test]
	public void disconnecting_a_client_retries_inflight_messages_immediately() {
		var client1Envelope = new FakeEnvelope();
		var client2Envelope = new FakeEnvelope();

		var fakeCheckpointReader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(fakeCheckpointReader)
				.WithMessageParker(new FakeMessageParker())
				.PreferRoundRobin()
				.StartFromCurrent()
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { })));

		fakeCheckpointReader.Load(null);

		sub.AddClient(Guid.NewGuid(), Guid.NewGuid(), "connection-1", client1Envelope, 10, "foo", "bar");
		var connectionId = Guid.NewGuid();
		sub.AddClient(Guid.NewGuid(), connectionId, "connection-2", client2Envelope, 10, "foo", "bar");

		Assert.IsTrue(sub.HasClients);
		Assert.AreEqual(2, sub.ClientCount);

		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(0, _eventSource));
		sub.NotifyLiveSubscriptionMessage(Helper.GetFakeEventFor(1, _eventSource));

		Assert.AreEqual(1, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		Assert.IsTrue(sub.RemoveClientByConnectionId(connectionId));

		Assert.AreEqual(1, sub.ClientCount);

		// Message 2 should be retried on client 1 as it wasn't acked.
		Assert.AreEqual(2, client1Envelope.Replies.Count);
		Assert.AreEqual(1, client2Envelope.Replies.Count);

		// Retry count should have increased
		Assert.AreEqual(1,
			((ClientMessage.PersistentSubscriptionStreamEventAppeared)client1Envelope.Replies.Last()).RetryCount);
	}

	[Test]
	public void disconnecting_a_client_with_no_persistent_subscription() {
		var fakeCheckpointReader = new FakeCheckpointReader();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(fakeCheckpointReader)
				.WithMessageParker(new FakeMessageParker())
				.PreferRoundRobin()
				.StartFromCurrent()
				.WithCheckpointWriter(new FakeCheckpointWriter(x => { })));

		fakeCheckpointReader.Load(null);

		var clientConnectionId = Guid.NewGuid();

		Assert.IsFalse(sub.RemoveClientByConnectionId(clientConnectionId));
	}
}

[TestFixture(EventSource.SingleStream)]
[TestFixture(EventSource.AllStream)]
[TestFixture(EventSource.FilteredAllStream)]
public class ParkTests {
	private readonly EventSource _eventSource;
	public ParkTests(EventSource eventSource) {
		_eventSource = eventSource;
	}

	[Test]
	public void retrying_parked_messages_with_empty_stream_should_allow_retrying_parked_messages_again() {
		//setup the persistent subscription
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.StartFromBeginning());
		reader.Load(null);

		Assert.AreEqual(0, parker.BeginReadEndSequenceCount);

		//retry all parked messages
		sub.RetryParkedMessages(null);

		//this should invoke message parker's BeginReadEndSequence
		Assert.AreEqual(1, parker.BeginReadEndSequenceCount);

		//retry all parked messages again
		sub.RetryParkedMessages(null);

		//this should invoke message parker's BeginReadEndSequence again
		Assert.AreEqual(2, parker.BeginReadEndSequenceCount);
	}

	[Test]
	public void retrying_parked_messages_with_stop_at_invokes_readend() {
		//setup the persistent subscription
		var reader = new FakeCheckpointReader();
		var parker = new FakeMessageParker();
		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader())
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(parker)
				.StartFromBeginning());
		reader.Load(null);

		Assert.AreEqual(0, parker.BeginReadEndSequenceCount);

		//retry parked messages stop at version 5000
		sub.RetryParkedMessages(5000);

		//this should invoke message parker's BeginReadEndSequence
		Assert.AreEqual(1, parker.BeginReadEndSequenceCount);
	}


	[Test]
	public void retrying_parked_messages_without_stop_at_replays_all_parkedEvents() {
		var messageParker = new FakeMessageParker();
		var reader = new FakeCheckpointReader();

		List<int> loadCount = new List<int>();

		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader((stream, startEventNumber, countToLoad, batchSize,
					maxWindowSize, resolveLinkTos, skipFirstEvent, onEventsFound) => loadCount.Add(countToLoad)))
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(messageParker)
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(1));
		reader.Load(null);

		loadCount.Clear(); // clear loads from checkpoint reader

		var clientConnectionId = Guid.NewGuid();
		var clientCorrelationId = Guid.NewGuid();
		sub.AddClient(clientCorrelationId, clientConnectionId, "connection-1", new FakeEnvelope(), 50, "foo", "bar");

		//park 19 events
		var parkedEvents = Enumerable.Range(0, 19)
			.Select(v => Helper.BuildFakeEvent(Guid.NewGuid(), "type", "$persistentsubscription-streamName::groupName-parked", v, v, v)).ToArray();
		foreach (var parkedEvent in parkedEvents) {
			messageParker.BeginParkMessage(parkedEvent, "parked", (ev, res) => { });
		}

		sub.RetryParkedMessages(null);

		Assert.AreEqual(20, loadCount[0]);

		sub.HandleParkedReadCompleted(
			parkedEvents,
			new PersistentSubscriptionSingleStreamPosition(19),
			true,
			20);

		Assert.AreEqual(19, messageParker.MarkedAsProcessed);
		Assert.AreEqual(19, sub.OutstandingMessageCount);

	}

	[Test]
	public void retrying_parked_messages_with_stop_at_replays_parkedEvents_until_that_version() {
		var messageParker = new FakeMessageParker();
		var reader = new FakeCheckpointReader();

		List<int> loadCount = new List<int>();

		var sub = new Core.Services.PersistentSubscription.PersistentSubscription(
			Helper.CreatePersistentSubscriptionBuilderFor(_eventSource)
				.WithEventLoader(new FakeStreamReader((stream, startEventNumber, countToLoad, batchSize, maxWindowSize, resolveLinkTos, skipFirstEvent, onEventsFound) => loadCount.Add(countToLoad)))
				.WithCheckpointReader(reader)
				.WithCheckpointWriter(new FakeCheckpointWriter(i => { }))
				.WithMessageParker(messageParker)
				.StartFromBeginning()
				.MinimumToCheckPoint(1)
				.MaximumToCheckPoint(1));
		reader.Load(null);

		loadCount.Clear(); // clear loads from checkpoint reader

		var clientConnectionId = Guid.NewGuid();
		var clientCorrelationId = Guid.NewGuid();
		sub.AddClient(clientCorrelationId, clientConnectionId, "connection-1", new FakeEnvelope(), 50, "foo", "bar");

		//park 19 events
		var parkedEvents = Enumerable.Range(0, 19)
			.Select(v => Helper.BuildFakeEvent(Guid.NewGuid(), "type", "$persistentsubscription-streamName::groupName-parked", v, v, v)).ToArray();
		foreach (var parkedEvent in parkedEvents) {
			messageParker.BeginParkMessage(parkedEvent, "parked", (ev, res) => { });
		}

		var stopAt = 7L;
		sub.RetryParkedMessages(stopAt);

		// stopAt == Count
		Assert.AreEqual(stopAt + 1, loadCount[0]);

		sub.HandleParkedReadCompleted(
			parkedEvents.Where(re => re.OriginalEventNumber < 7).ToArray(),
			new PersistentSubscriptionSingleStreamPosition(7),
			false,
			stopAt);

		Assert.AreEqual(7, messageParker.MarkedAsProcessed);

		Assert.AreEqual(7, sub.OutstandingMessageCount);

	}
}

[Ignore("very long test")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class DeadlockTest<TLogFormat, TStreamId> : SpecificationWithMiniNode<TLogFormat, TStreamId> {
	protected override Task Given() {
		_conn = BuildConnection(_node);
		return _conn.ConnectAsync();
	}

	protected override Task When() => Task.CompletedTask;

	[Test]
	public async Task read_whilst_ack_doesnt_deadlock_with_request_response_dispatcher() {
		var persistentSubscriptionSettings = PersistentSubscriptionSettings.Create().Build();
		var userCredentials = DefaultData.AdminCredentials;
		await _conn.CreatePersistentSubscriptionAsync("TestStream", "TestGroup", persistentSubscriptionSettings,
			userCredentials);

		const int count = 5000;
		await _conn.AppendToStreamAsync("TestStream", ExpectedVersion.Any, CreateEvent().Take(count));


		var received = 0;
		var manualResetEventSlim = new ManualResetEventSlim();
		var sub1 = _conn.ConnectToPersistentSubscription("TestStream", "TestGroup", (sub, ev) => {
			received++;
			if (received == count) {
				manualResetEventSlim.Set();
			}

			return Task.CompletedTask;
		},
			(sub, reason, ex) => { });
		Assert.IsTrue(manualResetEventSlim.Wait(TimeSpan.FromSeconds(30)),
			"Failed to receive all events in 2 minutes. Assume event store is deadlocked.");
		sub1.Stop(TimeSpan.FromSeconds(10));
		_conn.Close();
	}

	private static IEnumerable<EventData> CreateEvent() {
		while (true) {
			yield return new EventData(Guid.NewGuid(), "testtype", false, new byte[0], new byte[0]);
		}
	}
}

public static class Helper {
	public static PersistentSubscriptionParamsBuilder CreatePersistentSubscriptionBuilderFor(EventSource eventSource) {
		switch (eventSource) {
			case EventSource.SingleStream:
				return PersistentSubscriptionToStreamParamsBuilder.CreateFor("streamName", "groupName");
			case EventSource.AllStream:
				return PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName");
			case EventSource.FilteredAllStream:
				return PersistentSubscriptionToAllParamsBuilder.CreateFor("groupName",
					EventFilter.StreamName.Prefixes(true, "stream"));
			default:
				throw new InvalidOperationException();
		}
	}

	private static readonly Guid[] eventIds = {
		//could be generated with a pseudo random number generator but
		//there's no need for now
		Guid.Parse("157e9b5a-09b1-49a8-9409-000000000000"),
		Guid.Parse("e986669f-1845-4a2b-87df-111111111111"),
		Guid.Parse("c18d692f-ab16-401f-b020-222222222222"),
		Guid.Parse("2d31ce08-6a5a-48df-b92b-333333333333"),
		Guid.Parse("aa343d98-41d4-4d9d-8356-444444444444"),
		Guid.Parse("3bd03065-d0e1-460f-bf2d-555555555555"),
		Guid.Parse("e18d84f3-72ea-4912-84ac-666666666666"),
		Guid.Parse("8ee1ee33-07f7-42e7-a64d-777777777777"),
		Guid.Parse("1594759b-b419-48a4-8a84-888888888888"),
		Guid.Parse("0a07af23-6b6b-47c7-93c4-999999999999")
	};

	public static Guid GetEventIdFor(int position) {
		if (position >= eventIds.Length) {
			throw new InvalidOperationException();
		}

		return eventIds[position];
	}

	public static ResolvedEvent GetFakeEventFor(int position, EventSource eventSource, Guid? forcedEventId = null,
		string streamPrefix = "", string eventType = "type") {
		var eventId = forcedEventId ?? GetEventIdFor(position);
		switch (eventSource) {
			case EventSource.SingleStream:
				var streamName = string.IsNullOrEmpty(streamPrefix) ? "streamName" : streamPrefix;
				return BuildFakeEvent(eventId, eventType, streamName, position, 1234, 1234);
			case EventSource.AllStream:
			case EventSource.FilteredAllStream:
				streamPrefix = string.IsNullOrEmpty(streamPrefix) ? "stream" : streamPrefix;
				string stream = streamPrefix + (position % 2);
				int eventNumber = (position / 2) + (position % 2 == 0 ? 2 : 0); //intentionally add offset to make event positions not continuous across all streams
				var streamPos = GetStreamPositionFor(position, eventSource);
				return BuildFakeEvent(eventId, eventType, stream, eventNumber, streamPos.TFPosition.Commit, streamPos.TFPosition.Prepare);
			default:
				throw new InvalidOperationException();
		}
	}

	public static IPersistentSubscriptionStreamPosition GetStreamPositionFor(int position, EventSource eventSource) {
		return eventSource switch {
			EventSource.SingleStream => new PersistentSubscriptionSingleStreamPosition(position),
			EventSource.AllStream => new PersistentSubscriptionAllStreamPosition(1234 + position * 2, 1233 + position * 2),
			EventSource.FilteredAllStream => new PersistentSubscriptionAllStreamPosition(1234 + position * 2, 1233 + position * 2),
			_ => throw new InvalidOperationException()
		};
	}

	public static ResolvedEvent BuildFakeEvent(Guid id, string type, string stream, long version, long commitPosition=1234567, long preparePosition=1234567) {
		return BuildFakeEventWithMetadata(id, type, stream, version, new byte[0], commitPosition, preparePosition);
	}

	public static ResolvedEvent BuildFakeEventWithMetadata(Guid id, string type, string stream, long version, byte[] metaData, long commitPosition=1234567, long preparePosition=1234567) {
		return
			ResolvedEvent.ForUnresolvedEvent(new EventRecord(version, preparePosition, Guid.NewGuid(), id, commitPosition, 1234,
				stream, version,
				DateTime.UtcNow, PrepareFlags.SingleWrite, type, new byte[0], metaData), commitPosition);
	}

	public static ResolvedEvent BuildLinkEvent(Guid id, string stream, long version, ResolvedEvent ev, bool resolved = true, long commitPosition=1234567, long preparePosition=1234567) {
		var link = new EventRecord(version, preparePosition, Guid.NewGuid(), id, commitPosition, 1234, stream, version,
			DateTime.UtcNow, PrepareFlags.SingleWrite, SystemEventTypes.LinkTo,
			Encoding.UTF8.GetBytes(string.Format("{0}@{1}", ev.OriginalEventNumber, ev.OriginalStreamId)),
			new byte[0]);
		if (resolved)
			return ResolvedEvent.ForResolvedLink(ev.Event, link, commitPosition);
		else
			return ResolvedEvent.ForUnresolvedEvent(link, commitPosition);
	}
}

class FakeStreamReader : IPersistentSubscriptionStreamReader {
	private readonly Action<IPersistentSubscriptionEventSource, IPersistentSubscriptionStreamPosition, int, int, int, bool, bool,
		Action<ResolvedEvent[], IPersistentSubscriptionStreamPosition, bool>> _action = null;

	public FakeStreamReader() {
	}

	public FakeStreamReader(Action<IPersistentSubscriptionEventSource, IPersistentSubscriptionStreamPosition, int, int, int, bool, bool, Action<ResolvedEvent[], IPersistentSubscriptionStreamPosition, bool>> action) {
		_action = action;
	}

	public void BeginReadEvents(IPersistentSubscriptionEventSource stream, IPersistentSubscriptionStreamPosition startEventNumber,
		int countToLoad, int batchSize, int maxWindowSize, bool resolveLinkTos, bool skipFirstEvent,
		Action<IReadOnlyList<ResolvedEvent>, IPersistentSubscriptionStreamPosition, bool> onEventsFound,
		Action<IPersistentSubscriptionStreamPosition, long> onEventsSkipped,
		Action<string> onError) {
		if (_action != null) {
			_action(stream, startEventNumber, countToLoad, batchSize, maxWindowSize, resolveLinkTos, skipFirstEvent, onEventsFound);
		}
	}
}

class FakeCheckpointReader : IPersistentSubscriptionCheckpointReader {
	private Action<string> _onStateLoaded;

	public void BeginLoadState(string subscriptionId, Action<string> onStateLoaded) {
		_onStateLoaded = onStateLoaded;
	}

	public void Load(string state) {
		_onStateLoaded(state);
	}
}

class FakeMessageParker : IPersistentSubscriptionMessageParker {
	private Action<ResolvedEvent, OperationResult> _parkMessageCompleted;
	public List<ResolvedEvent> ParkedEvents = new List<ResolvedEvent>();
	private readonly Action _deleteAction;
	private long _lastParkedEventNumber = -1;
	private long _lastTruncateBefore = -1;
	public int BeginReadEndSequenceCount { get; private set; } = 0;

	public FakeMessageParker() {
	}

	public FakeMessageParker(Action deleteAction) {
		_deleteAction = deleteAction;
	}

	public long MarkedAsProcessed { get; private set; }

	public void ParkMessageCompleted(int idx, OperationResult result) {
		_parkMessageCompleted?.Invoke(ParkedEvents[idx], result);
	}

	public void BeginParkMessage(ResolvedEvent ev, string reason,
		Action<ResolvedEvent, OperationResult> completed) {
		ParkedEvents.Add(ev);
		_lastParkedEventNumber = ev.OriginalEventNumber;
		_parkMessageCompleted = completed;
	}

	public void BeginReadEndSequence(Action<long?> completed) {
		BeginReadEndSequenceCount++;
		if (_lastParkedEventNumber == -1)
			completed(null); //NoStream
		else
			completed(_lastParkedEventNumber);
	}

	public void BeginMarkParkedMessagesReprocessed(long sequence, DateTime? dateTime, bool updateOldestParkedMessage) {
		MarkedAsProcessed = sequence;
	}

	public void BeginDelete(Action<IPersistentSubscriptionMessageParker> completed) {
		_deleteAction?.Invoke();
	}

	public long ParkedMessageCount {
		get {
			return _lastParkedEventNumber == -1 ? 0 :
				_lastTruncateBefore == -1 ? _lastParkedEventNumber + 1 :
				_lastParkedEventNumber - _lastTruncateBefore + 1;
		}
	}

	public void BeginLoadStats(Action completed) {
		completed();
	}

	public DateTime? GetOldestParkedMessage { get; }
}


class FakeCheckpointWriter : IPersistentSubscriptionCheckpointWriter {
	private readonly Action<IPersistentSubscriptionStreamPosition> _action;
	private readonly Action _deleteAction;

	public FakeCheckpointWriter(Action<IPersistentSubscriptionStreamPosition> action, Action deleteAction = null) {
		_action = action;
		_deleteAction = deleteAction;
	}

	public void BeginWriteState(IPersistentSubscriptionStreamPosition state) {
		_action(state);
	}

	public void BeginDelete(Action<IPersistentSubscriptionCheckpointWriter> completed) {
		_deleteAction?.Invoke();
	}
}
