// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Projections.Core.XUnit.Tests.TestHelpers;

// TODO: Flesh out this helper as more tests need it
public class ExistingStreamsHelper {
	private readonly Dictionary<string, List<ExistingEvent>> _streams = new();
	private readonly List<string> _hardDeletedStreams = [];
	private long _lastPosition;

	public void AddEvents(params ExistingEvent[] newEvents) {
		foreach (var newEvent in newEvents) {
			if (_streams.TryGetValue(newEvent.EventStreamId, out var existingEvents)) {
				existingEvents.Add(newEvent);
			} else {
				_streams.Add(newEvent.EventStreamId, [newEvent]);
			}

			if (newEvent.PreparePosition > _lastPosition) {
				_lastPosition = newEvent.PreparePosition;
			}
		}
	}

	public void HardDeleteStreams(string[] deletedStreams) {
		foreach (var stream in deletedStreams) {
			if (!_hardDeletedStreams.Contains(stream)) {
				_hardDeletedStreams.Add(stream);
			}
		}
	}

	public long GetLastEventNumberForStream(string streamId) {
		if (_hardDeletedStreams.Contains(streamId)) {
			return EventNumber.DeletedStream;
		}

		return _streams.TryGetValue(streamId, out var existingEvents)
			? existingEvents.Max(x => x.EventNumber)
			: EventNumber.Invalid;
	}

	public ReadStreamEventsBackwardCompleted ReadStreamBackward(
		ReadStreamEventsBackward request) {
		if (_hardDeletedStreams.Contains(request.EventStreamId)) {
			return CreateReadBackwardCompleted(request, ReadStreamResult.StreamDeleted, []);
		}

		if (!_streams.TryGetValue(request.EventStreamId, out var existingEvents)) {
			return CreateReadBackwardCompleted(request, ReadStreamResult.NoStream, []);
		}

		var resolvedEvents = new List<ResolvedEvent>();
		var lastEventNumber = GetLastEventNumberForStream(request.EventStreamId);
		var current = request.FromEventNumber == -1 ? lastEventNumber : request.FromEventNumber;
		for (var i = 0; i < request.MaxCount; i++) {
			var foundEvent = existingEvents.FirstOrDefault(x => x.EventNumber == current);
			if (foundEvent is not null) {
				resolvedEvents.Add(ResolvedEvent.ForUnresolvedEvent(foundEvent.ToEventRecord(request.CorrelationId)));
			}

			current--;
		}

		return CreateReadBackwardCompleted(request, ReadStreamResult.Success, resolvedEvents.ToArray());
	}

	private ReadStreamEventsBackwardCompleted CreateReadBackwardCompleted(ReadStreamEventsBackward request, ReadStreamResult result, ResolvedEvent[] events) {
		if (result is ReadStreamResult.NoStream) {
			return new(request.CorrelationId, request.EventStreamId,
				request.FromEventNumber, request.MaxCount, result, [], StreamMetadata.Empty, false,
				"", -1, EventNumber.Invalid, true, _lastPosition);
		}

		long nextEventNumber = 0;
		if (events.Length > 0) {
			nextEventNumber = events.Last().OriginalEventNumber;
		}

		var isEof = nextEventNumber == 0;
		return new(
			request.CorrelationId, request.EventStreamId, request.FromEventNumber, request.MaxCount,
			ReadStreamResult.Success, events.ToArray(), StreamMetadata.Empty, false, string.Empty,
			nextEventNumber, GetLastEventNumberForStream(request.EventStreamId), isEof, _lastPosition);
	}

	public class ExistingEvent(
		string eventStreamId,
		long eventNumber,
		long position,
		string data,
		string metadata = "",
		string eventType = "test-event") {
		public string EventStreamId { get; } = eventStreamId;
		public string EventType { get; } = eventType;
		public long EventNumber { get; } = eventNumber;
		public long PreparePosition { get; } = position;
		public string Data { get; } = data;
		public string Metadata { get; } = metadata;

		public EventRecord ToEventRecord(Guid correlationId) =>
			new(EventNumber, PreparePosition, correlationId, Guid.NewGuid(),
				transactionPosition: 0, transactionOffset: 0,
				EventStreamId, EventNumber, DateTime.Now, PrepareFlags.IsCommitted, EventType,
				Encoding.UTF8.GetBytes(Data), Encoding.UTF8.GetBytes(Metadata));
	}
}
