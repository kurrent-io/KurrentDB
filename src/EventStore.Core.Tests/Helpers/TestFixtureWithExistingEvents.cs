// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace EventStore.Core.Tests.Helpers;

public abstract class TestFixtureWithExistingEvents<TLogFormat,TStreamId> : TestFixtureWithReadWriteDispatchers,
	IHandle<ClientMessage.ReadStreamEventsBackward>,
	IHandle<ClientMessage.ReadStreamEventsForward>,
	IHandle<ClientMessage.ReadAllEventsForward>,
	IHandle<ClientMessage.ReadAllEventsBackward>,
	IHandle<ClientMessage.FilteredReadAllEventsForward>,
	IHandle<ClientMessage.FilteredReadAllEventsBackward>,
	IHandle<ClientMessage.WriteEvents>,
	IHandle<ClientMessage.TransactionStart>,
	IHandle<ClientMessage.TransactionWrite>,
	IHandle<ClientMessage.TransactionCommit>,
	IHandle<ClientMessage.SubscribeToStream>,
	IHandle<ClientMessage.FilteredSubscribeToStream>,
	IHandle<ClientMessage.DeleteStream>,
	IHandle<StorageMessage.EventCommitted>{
	public class Transaction {
		private readonly ClientMessage.TransactionStart _startMessage;

		private readonly List<Tuple<int, Event>> _events = new();

		public Transaction(long position, ClientMessage.TransactionStart startMessage) {
			_startMessage = startMessage;
		}

		public void Write(ClientMessage.TransactionWrite message, ref int fakePosition) {
			foreach (var @event in message.Events) {
				_events.Add(Tuple.Create(fakePosition, @event));
				fakePosition += 50;
			}
		}

		public void Commit(ClientMessage.TransactionCommit message, TestFixtureWithExistingEvents<TLogFormat,TStreamId> fixture) {
			var commitPosition = fixture._fakePosition;
			fixture._fakePosition += 50;
			fixture.ProcessWrite(
				message.Envelope, message.CorrelationId, _startMessage.EventStreamId, _startMessage.ExpectedVersion,
				_events.Select(v => v.Item2).ToArray(),
				(f, l) =>
					new ClientMessage.TransactionCommitCompleted(
						message.CorrelationId, message.TransactionId, f, l, -1, -1),
				new ClientMessage.TransactionCommitCompleted(
					message.CorrelationId, message.TransactionId, OperationResult.WrongExpectedVersion,
					"Wrong expected version"),
				_events.Select(v => (long)v.Item1).ToArray(), commitPosition);
		}
	}

	protected TestHandler<ClientMessage.ReadStreamEventsBackward> _listEventsHandler;

	protected readonly Dictionary<string, List<EventRecord>> _streams =
		new Dictionary<string, List<EventRecord>>();

	protected readonly SortedList<TFPos, EventRecord> _all = new SortedList<TFPos, EventRecord>();

	protected readonly HashSet<string> _deletedStreams = new HashSet<string>();

	protected readonly Dictionary<long, Transaction> _activeTransactions = new Dictionary<long, Transaction>();

	private int _fakePosition = 100;
	private bool _allWritesSucceed;
	private readonly HashSet<string> _writesToSucceed = new HashSet<string>();
	private bool _allWritesQueueUp;
	private Queue<ClientMessage.WriteEvents> _writesQueue;
	private Queue<ClientMessage.ReadStreamEventsBackward> _readEventsBackwardsQueue;
	private bool _readsBackwardsQueuesUp;
	private bool _readAllEnabled;
	private bool _noOtherStreams;
	private bool _notReady;
	private bool _readsTimeOut;
	protected List<DateTime> EventTimeStamps = new List<DateTime>();

	protected readonly HashSet<string> _readsToTimeOutOnce = new HashSet<string>();

	private static readonly char[] _linkToSeparator = new[] {'@'};

	protected TFPos ExistingStreamMetadata(string streamId, string metadata) {
		return ExistingEvent("$$" + streamId, SystemEventTypes.StreamMetadata, "", metadata, isJson: true);
	}

	protected TFPos ExistingEvent(string streamName, string eventType, string eventMetadata, string eventData,
		bool isJson = false) {
		return WriteEvent(streamName, eventType, eventMetadata, eventData, isJson).Item2;
	}

	protected EventRecord ExistingEventTimeStamp(string streamName, string eventType, string eventMetadata, string eventData,
		bool isJson = false) {
		return WriteEvent(streamName, eventType, eventMetadata, eventData, isJson).Item1;
	}

	protected (EventRecord, TFPos) WriteEvent(string streamName, string eventType, string eventMetadata, string eventData,
		bool isJson = false) {
		List<EventRecord> list;
		if (!_streams.TryGetValue(streamName, out list) || list == null) {
			list = new List<EventRecord>();
			_streams[streamName] = list;
		}

		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamIdIgnored = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeIdIgnored = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var eventRecord = new EventRecord(
			list.Count,
			LogRecord.Prepare(
				recordFactory,
				_fakePosition, Guid.NewGuid(), Guid.NewGuid(), _fakePosition, 0, streamIdIgnored, list.Count - 1,
				PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd | (isJson ? PrepareFlags.IsJson : 0),
				eventTypeIdIgnored, eventData is null ? null : Helper.UTF8NoBom.GetBytes(eventData),
				eventMetadata == null ? new byte[0] : Helper.UTF8NoBom.GetBytes(eventMetadata),
				_timeProvider.UtcNow), streamName, eventType);
		list.Add(eventRecord);
		var eventPosition = new TFPos(_fakePosition + 50, _fakePosition);
		_all.Add(eventPosition, eventRecord);
		_fakePosition += 100;
		return (eventRecord, eventPosition);
	}

	protected void NotReady() {
		_notReady = true;
	}

	protected void Ready() {
		_notReady = false;
	}

	protected void AllReadsTimeOut() {
		_readsTimeOut = true;
	}

	protected void EnableReadAll() {
		_readAllEnabled = true;
	}

	protected void ReadsBackwardQueuesUp() {
		_readsBackwardsQueuesUp = true;
	}

	protected void CompleteOneReadBackwards() {
		var read = _readEventsBackwardsQueue.Dequeue();
		ProcessRead(read);
	}

	protected void NoStream(string streamId) {
		_streams[streamId] = null;
	}

	protected void NoOtherStreams() {
		_noOtherStreams = true;
	}

	protected void DeletedStream(string streamId) {
		_deletedStreams.Add(streamId);
		ExistingStreamMetadata(streamId, CreateStreamDeletedEventJson());
	}

	private static string CreateStreamDeletedEventJson() {
		return new StreamMetadata(null, null, EventNumber.DeletedStream, null, null, null).ToJsonString();
	}

	protected void AllWritesSucceed(bool succeed = true) {
		_allWritesSucceed = succeed;
	}

	protected void AllWritesToSucceed(string streamId) {
		_writesToSucceed.Add(streamId);
	}

	protected void AllWritesQueueUp() {
		_allWritesQueueUp = true;
	}

	protected void TimeOutReadToStreamOnce(string streamId) {
		_readsToTimeOutOnce.Add(streamId);
	}

	protected void OneWriteCompletes() {
		var message = _writesQueue.Dequeue();
		ProcessWrite(
			message.Envelope, message.CorrelationId, message.EventStreamId, message.ExpectedVersion, message.Events,
			(firstEventNumber, lastEventNumber) =>
				new ClientMessage.WriteEventsCompleted(message.CorrelationId, firstEventNumber, lastEventNumber, -1,
					-1),
			new ClientMessage.WriteEventsCompleted(
				message.CorrelationId, OperationResult.WrongExpectedVersion, "wrong expected version"));
	}

	protected void CompleteWriteWithResult(OperationResult result) {
		var message = _writesQueue.Dequeue();
		ProcessWrite(
			message.Envelope, message.CorrelationId, message.EventStreamId, ExpectedVersion.Any, message.Events,
			(firstEventNumber, lastEventNumber) =>
				new ClientMessage.WriteEventsCompleted(message.CorrelationId, result, String.Empty),
			new ClientMessage.WriteEventsCompleted(
				message.CorrelationId, OperationResult.WrongExpectedVersion, "wrong expected version"));
	}

	protected void AllWriteComplete() {
		while (_writesQueue.Count > 0)
			OneWriteCompletes();
	}

	[SetUp]
	public void setup1() {
		_writesQueue = new Queue<ClientMessage.WriteEvents>();
		_readEventsBackwardsQueue = new Queue<ClientMessage.ReadStreamEventsBackward>();
		_listEventsHandler = new TestHandler<ClientMessage.ReadStreamEventsBackward>();
		_bus.Subscribe(_listEventsHandler);
		_bus.Subscribe<ClientMessage.WriteEvents>(this);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackward>(this);
		_bus.Subscribe<ClientMessage.ReadStreamEventsForward>(this);
		_bus.Subscribe<ClientMessage.ReadAllEventsForward>(this);
		_bus.Subscribe<ClientMessage.ReadAllEventsBackward>(this);
		_bus.Subscribe<ClientMessage.FilteredReadAllEventsForward>(this);
		_bus.Subscribe<ClientMessage.FilteredReadAllEventsBackward>(this);
		_bus.Subscribe<ClientMessage.SubscribeToStream>(this);
		_bus.Subscribe<ClientMessage.FilteredSubscribeToStream>(this);
		_bus.Subscribe<ClientMessage.DeleteStream>(this);
		_bus.Subscribe<ClientMessage.TransactionStart>(this);
		_bus.Subscribe<ClientMessage.TransactionWrite>(this);
		_bus.Subscribe<ClientMessage.TransactionCommit>(this);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_readDispatcher);
		_bus.Subscribe<ClientMessage.NotHandled>(_readDispatcher);
		_bus.Subscribe<StorageMessage.EventCommitted>(this);
		_bus.Subscribe(_writeDispatcher);
		_bus.Subscribe(_ioDispatcher.StreamDeleter);
		_bus.Subscribe(_ioDispatcher.Awaker);
		_streams.Clear();
		_deletedStreams.Clear();
		_all.Clear();
		_readAllEnabled = false;
		_fakePosition = 100;
		_activeTransactions.Clear();
		Given1();
		Given();
	}

	protected virtual void Given1() {
	}

	protected virtual void Given() {
	}

	public void Handle(ClientMessage.ReadStreamEventsBackward message) {
		if (_notReady) {
			message.Envelope.ReplyWith(new ClientMessage.NotHandled(
				message.CorrelationId,
				ClientMessage.NotHandled.Types.NotHandledReason.NotReady,
				default(string)));
		}

		if (_readsBackwardsQueuesUp) {
			_readEventsBackwardsQueue.Enqueue(message);
			return;
		}

		if (_readsTimeOut) return;
		if (_readsToTimeOutOnce.Contains(message.EventStreamId)) {
			Console.WriteLine("[TEST] Timing out read backwards for {0}", message.EventStreamId);
			_readsToTimeOutOnce.Remove(message.EventStreamId);
			return;
		}

		ProcessRead(message);
	}

	private void ProcessRead(ClientMessage.ReadStreamEventsBackward message) {
		List<EventRecord> list;
		if (_deletedStreams.Contains(message.EventStreamId)) {
			message.Envelope.ReplyWith(
				new ClientMessage.ReadStreamEventsBackwardCompleted(
					message.CorrelationId, message.EventStreamId, message.FromEventNumber, message.MaxCount,
					ReadStreamResult.StreamDeleted, new ResolvedEvent[0], null, false, string.Empty, -1,
					EventNumber.DeletedStream, true, _fakePosition));
		} else if (_streams.TryGetValue(message.EventStreamId, out list) || _noOtherStreams) {
			if (list != null && list.Count > 0 && (list.Last().EventNumber >= message.FromEventNumber)
			    || (message.FromEventNumber == -1)) {
				ResolvedEvent[] records =
					list.Safe()
						.Reverse()
						.SkipWhile(v => message.FromEventNumber != -1 && v.EventNumber > message.FromEventNumber)
						.Take(message.MaxCount)
						.Select(v => BuildEvent(v, message.ResolveLinkTos))
						.ToArray();
				message.Envelope.ReplyWith(
					new ClientMessage.ReadStreamEventsBackwardCompleted(
						message.CorrelationId, message.EventStreamId,
						message.FromEventNumber == -1
							? (EnumerableExtensions.IsEmpty(list) ? -1 : list.Last().EventNumber)
							: message.FromEventNumber, message.MaxCount, ReadStreamResult.Success, records, null,
						false,
						string.Empty,
						nextEventNumber: records.Length > 0 ? records.Last().OriginalEvent.EventNumber - 1 : -1,
						lastEventNumber: list.Safe().Any() ? list.Safe().Last().EventNumber : -1,
						isEndOfStream: records.Length == 0 || records.Last().OriginalEvent.EventNumber == 0,
						tfLastCommitPosition: _fakePosition));
			} else {
				if (list == null) {
					message.Envelope.ReplyWith(
						new ClientMessage.ReadStreamEventsBackwardCompleted(
							message.CorrelationId, message.EventStreamId, message.FromEventNumber, message.MaxCount,
							ReadStreamResult.NoStream, new ResolvedEvent[0], null, false, "", nextEventNumber: -1,
							lastEventNumber: -1,
							isEndOfStream: true,
							tfLastCommitPosition: _fakePosition));
					return;
				}

				throw new NotImplementedException();
			}
		}
	}

	public void Handle(ClientMessage.ReadStreamEventsForward message) {
		if (_readsTimeOut) return;
		if (_readsToTimeOutOnce.Contains(message.EventStreamId)) {
			Console.WriteLine("[TEST] Timing out read forwards for {0}", message.EventStreamId);
			_readsToTimeOutOnce.Remove(message.EventStreamId);
			return;
		}

		List<EventRecord> list;
		if (_deletedStreams.Contains(message.EventStreamId)) {
			message.Envelope.ReplyWith(
				new ClientMessage.ReadStreamEventsBackwardCompleted(
					message.CorrelationId, message.EventStreamId, message.FromEventNumber, message.MaxCount,
					ReadStreamResult.StreamDeleted, new ResolvedEvent[0], null, false, string.Empty, -1,
					EventNumber.DeletedStream, true, _fakePosition));
		} else if (_streams.TryGetValue(message.EventStreamId, out list) || _noOtherStreams) {
			if (list != null && list.Count > 0 && message.FromEventNumber >= 0) {
				ResolvedEvent[] records =
					list.Safe()
						.SkipWhile(v => v.EventNumber < message.FromEventNumber)
						.Take(message.MaxCount)
						.Select(v => BuildEvent(v, message.ResolveLinkTos))
						.ToArray();
				var lastEventNumber = list.Safe().Any() ? list.Safe().Last().EventNumber : -1;
				message.Envelope.ReplyWith(
					new ClientMessage.ReadStreamEventsForwardCompleted(
						message.CorrelationId, message.EventStreamId,
						message.FromEventNumber, message.MaxCount, ReadStreamResult.Success, records, null, false,
						string.Empty,
						nextEventNumber: records.Length > 0
							? records.Last().OriginalEvent.EventNumber + 1
							: lastEventNumber + 1,
						lastEventNumber: lastEventNumber,
						isEndOfStream: records.Length == 0 ||
						               records.Last().OriginalEvent.EventNumber == list.Last().EventNumber,
						tfLastCommitPosition: _fakePosition));
			} else {
				if (list == null) {
					message.Envelope.ReplyWith(
						new ClientMessage.ReadStreamEventsForwardCompleted(
							message.CorrelationId, message.EventStreamId, message.FromEventNumber, message.MaxCount,
							ReadStreamResult.NoStream, new ResolvedEvent[0], null, false, "", nextEventNumber: -1,
							lastEventNumber: -1,
							isEndOfStream: true,
							tfLastCommitPosition: _fakePosition));
					return;
				}

				throw new NotImplementedException();
/*
                    message.Envelope.ReplyWith(
                            new ClientMessage.ReadStreamEventsBackwardCompleted(
                                    message.CorrelationId,
                                    message.EventStreamId,
                                    new EventLinkPair[0],
                                    ReadStreamResult.Success,
                                    nextEventNumber: -1,
                                    lastEventNumber: list.Safe().Last().EventNumber,
                                    isEndOfStream: true,// NOTE AN: don't know how to correctly determine this here
                                    lastCommitPosition: _lastPosition));
*/
			}
		}
	}

	private ResolvedEvent BuildEvent(EventRecord x, bool resolveLinks) {
		if (x.EventType == "$>" && resolveLinks) {
			var parts = Helper.UTF8NoBom.GetString(x.Data.Span).Split(_linkToSeparator, 2);
			List<EventRecord> list;
			if (_deletedStreams.Contains(parts[1]) || !_streams.TryGetValue(parts[1], out list))
				return ResolvedEvent.ForFailedResolvedLink(x, ReadEventResult.StreamDeleted);
			var eventNumber = int.Parse(parts[0]);
			var target = list[eventNumber];

			return ResolvedEvent.ForResolvedLink(target, x);
		} else
			return ResolvedEvent.ForUnresolvedEvent(x);
	}

	private ResolvedEvent BuildEvent(EventRecord x, bool resolveLinks, long commitPosition) {
		if (x.EventType == "$>" && resolveLinks) {
			var parts = Helper.UTF8NoBom.GetString(x.Data.Span).Split(_linkToSeparator, 2);
			var list = _streams[parts[1]];
			var eventNumber = int.Parse(parts[0]);
			var target = list[eventNumber];

			return ResolvedEvent.ForResolvedLink(target, x, commitPosition);
		} else
			return ResolvedEvent.ForUnresolvedEvent(x, commitPosition);
	}

	public void Handle(ClientMessage.WriteEvents message) {
		if (_allWritesSucceed || _writesToSucceed.Contains(message.EventStreamId)) {
			ProcessWrite(
				message.Envelope, message.CorrelationId, message.EventStreamId, message.ExpectedVersion,
				message.Events,
				(firstEventNumber, lastEventNumber) =>
					new ClientMessage.WriteEventsCompleted(message.CorrelationId, firstEventNumber, lastEventNumber,
						-1, -1),
				new ClientMessage.WriteEventsCompleted(
					message.CorrelationId, OperationResult.WrongExpectedVersion, "wrong expected version"));
		} else if (_allWritesQueueUp)
			_writesQueue.Enqueue(message);
	}

	private void ProcessWrite<T>(IEnvelope envelope, Guid correlationId, string streamId, long expectedVersion,
		Event[] events, Func<int, int, T> writeEventsCompleted, T wrongExpectedVersionResponse,
		long[] positions = null, long? commitPosition = null) where T : Message {
		if (positions == null) {
			positions = new long[events.Length];
			for (int i = 0; i < positions.Length; i++) {
				positions[i] = _fakePosition;
				_fakePosition += 100;
			}
		}

		List<EventRecord> list;
		if (!_streams.TryGetValue(streamId, out list) || list == null) {
			list = new List<EventRecord>();
			_streams[streamId] = list;
		}

		if (expectedVersion != EventStore.ClientAPI.ExpectedVersion.Any) {
			if (expectedVersion != list.Count - 1) {
				envelope.ReplyWith(wrongExpectedVersionResponse);
				return;
			}
		}

		var eventRecords = (from ep in events.Zip(positions, (@event, position) => new { @event, position })
			let e = ep.@event
			let eventNumber = list.Count
			//NOTE: ASSUMES STAYS ENUMERABLE
			let tfPosition = ep.position
			select
				new {
					position = tfPosition,
					record =
						new EventRecord(
							eventNumber, tfPosition, correlationId, e.EventId, tfPosition, 0, streamId,
							ExpectedVersion.Any, _timeProvider.UtcNow,
							PrepareFlags.SingleWrite | (e.IsJson ? PrepareFlags.IsJson : PrepareFlags.None),
							e.EventType, e.Data, e.Metadata)
				}); //NOTE: DO NOT MAKE ARRAY
		foreach (var eventRecord in eventRecords) {
			list.Add(eventRecord.record);
			var tfPos = new TFPos(commitPosition ?? eventRecord.position + 50, eventRecord.position);
			_all.Add(tfPos, eventRecord.record);
			_bus.Publish(
				new StorageMessage.EventCommitted(tfPos.CommitPosition, eventRecord.record, isTfEof: false));
		}

		_bus.Publish(new StorageMessage.TfEofAtNonCommitRecord());

		var firstEventNumber = list.Count - events.Length;
		envelope?.ReplyWith(writeEventsCompleted(firstEventNumber, firstEventNumber + events.Length - 1));
	}

	public void Handle(StorageMessage.EventCommitted msg) {
		var metadata = Json.ParseJson<ParkedMessageMetadata>(msg.Event.Metadata);
		if(metadata != null) EventTimeStamps.Add(metadata.Added.ToUniversalTime());
	}

	class ParkedMessageMetadata {
		public DateTime Added { get; set; }
		public string Reason { get; set; }
		public long SubscriptionEventNumber { get; set; }
	}

	public void Handle(ClientMessage.DeleteStream message) {
		List<EventRecord> list;
		if (_deletedStreams.Contains(message.EventStreamId)) {
			message.Envelope.ReplyWith(new ClientMessage.DeleteStreamCompleted(message.CorrelationId,
				OperationResult.StreamDeleted, string.Empty, -1, -1, -1));
			return;
		}

		if (!_streams.TryGetValue(message.EventStreamId, out list) || list == null) {
			if (message.ExpectedVersion == ExpectedVersion.Any) {
				message.Envelope.ReplyWith(new ClientMessage.DeleteStreamCompleted(message.CorrelationId,
					OperationResult.StreamDeleted, string.Empty, -1, -1, -1));
				_deletedStreams.Add(message.EventStreamId);
				return;
			}

			message.Envelope.ReplyWith(new ClientMessage.DeleteStreamCompleted(message.CorrelationId,
				OperationResult.WrongExpectedVersion, string.Empty));
			return;
		}

		_deletedStreams.Add(message.EventStreamId);

		ProcessWrite<Message>(
			null, message.CorrelationId, SystemStreams.MetastreamOf(message.EventStreamId), ExpectedVersion.Any,
			new Event[] {
				new Event(Guid.NewGuid(), SystemEventTypes.StreamMetadata, true, CreateStreamDeletedEventJson(),
					null)
			},
			null, null);

		message.Envelope.ReplyWith(new ClientMessage.DeleteStreamCompleted(message.CorrelationId,
			OperationResult.Success, string.Empty));
	}

	public void Handle(ClientMessage.ReadAllEventsForward message) {
		if (_readsTimeOut) return;
		if (!_readAllEnabled)
			return;
		var from = new TFPos(message.CommitPosition, message.PreparePosition);
		var records = _all.SkipWhile(v => v.Key < from).Take(message.MaxCount).ToArray();
		var list = new List<ResolvedEvent>();
		var pos = from;
		var next = pos;
		var prev = new TFPos(pos.CommitPosition, Int64.MaxValue);
		foreach (KeyValuePair<TFPos, EventRecord> record in records) {
			pos = record.Key;
			next = new TFPos(pos.CommitPosition, pos.PreparePosition + 1);
			list.Add(BuildEvent(record.Value, message.ResolveLinkTos, record.Key.CommitPosition));
		}

		var events = list.ToArray();
		message.Envelope.ReplyWith(
			new ClientMessage.ReadAllEventsForwardCompleted(
				message.CorrelationId, ReadAllResult.Success, "", events, null, false,
				message.MaxCount, pos, next, prev, _fakePosition));
	}

	public void Handle(ClientMessage.ReadAllEventsBackward message) {
		if (_readsTimeOut) return;
		if (!_readAllEnabled) return;

		var from = new TFPos(message.CommitPosition, message.PreparePosition);
		if (from == new TFPos(-1, -1)) // read from end
			from = new TFPos(long.MaxValue, long.MaxValue);

		var records = _all.Reverse().SkipWhile(v => v.Key > from).Take(message.MaxCount).ToArray();
		var list = new List<ResolvedEvent>();
		var pos = from;
		var next = pos;
		var prev = new TFPos(pos.CommitPosition, 0);
		foreach (KeyValuePair<TFPos, EventRecord> record in records) {
			pos = record.Key;
			next = new TFPos(pos.CommitPosition, pos.PreparePosition - 1);
			list.Add(BuildEvent(record.Value, message.ResolveLinkTos, record.Key.CommitPosition));
		}

		var events = list.ToArray();
		message.Envelope.ReplyWith(
			new ClientMessage.ReadAllEventsBackwardCompleted(
				message.CorrelationId, ReadAllResult.Success, "", events, null, false,
				message.MaxCount, pos, next, prev, _fakePosition));
	}

	public void Handle(ClientMessage.FilteredReadAllEventsForward message) {
		if (_readsTimeOut) return;
		if (!_readAllEnabled)
			return;
		var from = new TFPos(message.CommitPosition, message.PreparePosition);
		var records = _all.SkipWhile(v => v.Key < from).Where(kvp => message.EventFilter.IsEventAllowed(kvp.Value)).Take(message.MaxCount).ToArray();
		var list = new List<ResolvedEvent>();
		var pos = from;
		var next = pos;
		var prev = new TFPos(pos.CommitPosition, Int64.MaxValue);
		foreach (KeyValuePair<TFPos, EventRecord> record in records) {
			pos = record.Key;
			next = new TFPos(pos.CommitPosition, pos.PreparePosition + 1);
			list.Add(BuildEvent(record.Value, message.ResolveLinkTos, record.Key.CommitPosition));
		}

		var events = list.ToArray();
		message.Envelope.ReplyWith(
			new ClientMessage.FilteredReadAllEventsForwardCompleted(
				message.CorrelationId, FilteredReadAllResult.Success, "", events, null, false,
				message.MaxCount, pos, next, prev, _fakePosition, list.Count < message.MaxCount, -1));
	}

	public void Handle(ClientMessage.FilteredReadAllEventsBackward message) {
		if (_readsTimeOut) return;
		if (!_readAllEnabled) return;

		var from = new TFPos(message.CommitPosition, message.PreparePosition);
		if (from == new TFPos(-1, -1)) // read from end
			from = new TFPos(long.MaxValue, long.MaxValue);

		var records = _all.Reverse().SkipWhile(v => v.Key > from).Where(kvp => message.EventFilter.IsEventAllowed(kvp.Value)).Take(message.MaxCount).ToArray();
		var list = new List<ResolvedEvent>();
		var pos = from;
		var next = pos;
		var prev = new TFPos(pos.CommitPosition, 0);
		foreach (KeyValuePair<TFPos, EventRecord> record in records) {
			pos = record.Key;
			next = new TFPos(pos.CommitPosition, pos.PreparePosition - 1);
			list.Add(BuildEvent(record.Value, message.ResolveLinkTos, record.Key.CommitPosition));
		}

		var events = list.ToArray();
		message.Envelope.ReplyWith(
			new ClientMessage.FilteredReadAllEventsBackwardCompleted(
				message.CorrelationId, FilteredReadAllResult.Success, "", events, null, false,
				message.MaxCount, pos, next, prev, _fakePosition, list.Count < message.MaxCount));
	}

	public void Handle(ClientMessage.SubscribeToStream msg) {
		_streams.TryGetValue(msg.EventStreamId, out var list);

		var lastEventNumber = msg.EventStreamId.IsEmptyString()
			? (long?)null
			: list.Safe().Any() ? list.Safe().Last().EventNumber : -1;

		var lastIndexedPos = _all.IsEmpty() ? -1 : _all.Keys[^1].CommitPosition;
		var subscribedMessage =
			new ClientMessage.SubscriptionConfirmation(msg.CorrelationId, lastIndexedPos, lastEventNumber);
		msg.Envelope.ReplyWith(subscribedMessage);
	}

	public void Handle(ClientMessage.FilteredSubscribeToStream msg) {
		_streams.TryGetValue(msg.EventStreamId, out var list);

		var lastEventNumber = msg.EventStreamId.IsEmptyString()
			? (long?)null
			: list.Safe().Any() ? list.Safe().Last().EventNumber : -1;
		var lastCommitPos = _fakePosition;
		var subscribedMessage =
			new ClientMessage.SubscriptionConfirmation(msg.CorrelationId, lastCommitPos, lastEventNumber);
		msg.Envelope.ReplyWith(subscribedMessage);
	}

	public void Handle(ClientMessage.TransactionStart message) {
		var transactionId = _fakePosition;
		_activeTransactions.Add(transactionId, new Transaction(transactionId, message));
		_fakePosition += 50;
		message.Envelope.ReplyWith(
			new ClientMessage.TransactionStartCompleted(
				message.CorrelationId, transactionId, OperationResult.Success, ""));
	}

	public void Handle(ClientMessage.TransactionWrite message) {
		Transaction transaction;
		if (!_activeTransactions.TryGetValue(message.TransactionId, out transaction)) {
			message.Envelope.ReplyWith(
				new ClientMessage.TransactionWriteCompleted(
					message.CorrelationId, message.TransactionId, OperationResult.InvalidTransaction,
					"Transaction not found"));
		} else {
			transaction.Write(message, ref _fakePosition);
		}
	}

	public void Handle(ClientMessage.TransactionCommit message) {
		Transaction transaction;
		if (!_activeTransactions.TryGetValue(message.TransactionId, out transaction)) {
			message.Envelope.ReplyWith(
				new ClientMessage.TransactionWriteCompleted(
					message.CorrelationId, message.TransactionId, OperationResult.InvalidTransaction,
					"Transaction not found"));
		} else {
			transaction.Commit(message, this);
		}
	}

	protected TFPos GetTfPos(string streamId, long eventNumber) {
		return _all.Last(v => v.Value.EventStreamId == streamId && v.Value.EventNumber == eventNumber).Key;
	}

	public void AssertLastEvent(string streamId, string data, string message = null, int skip = 0) {
		message = message ?? string.Format("Invalid last event in the '{0}' stream. ", streamId);
		List<EventRecord> events;
		Assert.That(_streams.TryGetValue(streamId, out events), message + "The stream does not exist.");
		events = events.Take(events.Count - skip).ToList();
		Assert.IsNotEmpty(events, message + "The stream is empty.");
		var last = events[events.Count - 1];
		Assert.AreEqual(data, Encoding.UTF8.GetString(last.Data.Span));
	}

	public void AssertLastEventJson<T>(string streamId, T json, string message = null, int skip = 0) {
		message = message ?? string.Format("Invalid last event in the '{0}' stream. ", streamId);
		List<EventRecord> events;
		Assert.That(_streams.TryGetValue(streamId, out events), message + "The stream does not exist.");
		events = events.Take(events.Count - skip).ToList();
		Assert.IsNotEmpty(events, message + "The stream is empty.");
		var last = events[events.Count - 1];
		Assert.IsTrue((last.Flags & PrepareFlags.IsJson) != 0);
		HelperExtensions.AssertJson(json, last.Data.ParseJson<JObject>());
	}

	public void AssertLastEventIs(string streamId, string eventType, string message = null, int skip = 0) {
		message = message ?? string.Format("Invalid last event in the '{0}' stream. ", streamId);
		List<EventRecord> events;
		Assert.That(_streams.TryGetValue(streamId, out events), message + "The stream does not exist.");
		events = events.Take(events.Count - skip).ToList();
		Assert.IsNotEmpty(events, message + "The stream is empty.");
		var last = events[events.Count - 1];
		Assert.AreEqual(eventType, last.EventType);
	}

	public void AssertStreamTail(string streamId, params string[] data) {
		var message = string.Format("Invalid events in the '{0}' stream. ", streamId);
		List<EventRecord> events;
		Assert.That(_streams.TryGetValue(streamId, out events), message + "The stream does not exist.");
		var eventsText = events.Skip(events.Count - data.Length).Select(v => Encoding.UTF8.GetString(v.Data.Span))
			.ToList();
		if (data.Length > 0)
			Assert.IsNotEmpty(events, message + "The stream is empty.");

		Assert.That(
			data.SequenceEqual(eventsText),
			string.Format(
				"{0} does end with: {1} the tail is: {2}", streamId, data.Aggregate("", (a, v) => a + " " + v),
				eventsText.Aggregate("", (a, v) => a + " " + v)));
	}

	public void AssertStreamTailWithLinks(string streamId, params string[] data) {
		var message = string.Format("Invalid events in the '{0}' stream. ", streamId);
		List<EventRecord> events;
		Assert.That(_streams.TryGetValue(streamId, out events), message + "The stream does not exist.");
		var eventsText =
			events.Skip(events.Count - data.Length)
				.Select(v => new {Text = Encoding.UTF8.GetString(v.Data.Span), EventType = v.EventType})
				.Select(
					v =>
						v.EventType == SystemEventTypes.LinkTo
							? ResolveEventText(v.Text)
							: v.EventType + ":" + v.Text)
				.ToList();
		if (data.Length > 0)
			Assert.IsNotEmpty(events, message + "The stream is empty.");

		Assert.That(
			data.SequenceEqual(eventsText),
			string.Format(
				"{0} does not end with: {1}. the tail is: {2}", streamId, data.Aggregate("", (a, v) => a + " " + v),
				eventsText.Aggregate("", (a, v) => a + " " + v)));
	}

	private string ResolveEventText(string link) {
		var stream = SystemEventTypes.StreamReferenceEventToStreamId(SystemEventTypes.LinkTo, link);
		var eventNumber = SystemEventTypes.EventLinkToEventNumber(link);
		return _streams[stream][(int)eventNumber].EventType + ":"
		                                                    +
		                                                    Encoding.UTF8.GetString(
			                                                    _streams[stream][(int)eventNumber].Data.Span);
	}

	public void AssertStreamContains(string streamId, params string[] data) {
		var message = string.Format("Invalid events in the '{0}' stream. ", streamId);
		List<EventRecord> events;
		Assert.That(_streams.TryGetValue(streamId, out events), message + "The stream does not exist.");
		if (data.Length > 0)
			Assert.IsNotEmpty(events, message + "The stream is empty.");

		var eventsData = new HashSet<string>(events.Select(v => Encoding.UTF8.GetString(v.Data.Span)));
		var missing = data.Where(v => !eventsData.Contains(v)).ToArray();

		Assert.That(missing.Length == 0,
			string.Format("{0} does not contain: {1}", streamId, missing.Aggregate("", (a, v) => a + " " + v)));
	}

	public void AssertEvent(string streamId, long eventNumber, string data) {
		throw new NotImplementedException();
	}

	public void AssertEmptyStream(string streamId) {
		throw new NotImplementedException();
	}

	public void AssertEmptyOrNoStream(string streamId) {
		List<EventRecord> events;
		Assert.That(
			!_streams.TryGetValue(streamId, out events) || events.Count == 0,
			string.Format("The stream {0} should not exist.", streamId));
	}

	[Conditional("DEBUG")]
	public void DumpStream(string streamId) {
#if DEBUG
		if (_deletedStreams.Contains(streamId))
			Console.WriteLine("Stream '{0}' has been deleted", streamId);

		List<EventRecord> list;
		if (!_streams.TryGetValue(streamId, out list) || list == null)
			Console.WriteLine("Stream '{0}' does not exist", streamId);
		if (list != null) {
			for (int index = 0; index < list.Count; index++) {
				var record = list[index];
				try {
					Console.WriteLine("{0}: '{1}' ==> \r\n{2}", index, record.EventType, record.DebugDataView);
				} catch (Exception ex) {
					Console.WriteLine("EXCEPTION: {0}", ex);
				}
			}
		}
#endif
	}
}
