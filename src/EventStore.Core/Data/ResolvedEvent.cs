// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Data;

public readonly struct ResolvedEvent : IEquatable<ResolvedEvent> {
	public static readonly ResolvedEvent[] EmptyArray = new ResolvedEvent[0];
	public static readonly ResolvedEvent EmptyEvent = new ResolvedEvent();

	public readonly EventRecord Event;
	public readonly EventRecord Link;
	private readonly long? _originalEventCommitPosition;

	public EventRecord OriginalEvent {
		get { return Link ?? Event; }
	}

	/// <summary>
	/// Position of the OriginalEvent (unresolved link or event) if available
	/// </summary>
	public TFPos? OriginalPosition => Link is not null ? LinkPosition : EventPosition;

	public TFPos? EventPosition => CalculatePosition(Event);

	public TFPos? LinkPosition => CalculatePosition(Link);

	public readonly ReadEventResult ResolveResult;

	public string OriginalStreamId {
		get { return OriginalEvent.EventStreamId; }
	}

	public long OriginalEventNumber {
		get { return OriginalEvent.EventNumber; }
	}


	private ResolvedEvent(EventRecord @event, EventRecord link, long? commitPosition,
		ReadEventResult resolveResult = default(ReadEventResult)) {
		Event = @event;
		Link = link;
		_originalEventCommitPosition = commitPosition;
		ResolveResult = resolveResult;
	}

	public static ResolvedEvent ForUnresolvedEvent(EventRecord @event, long? commitPosition = null) {
		return new ResolvedEvent(@event, null, commitPosition);
	}

	public static ResolvedEvent ForResolvedLink(EventRecord @event, EventRecord link, long? commitPosition = null) {
		return new ResolvedEvent(@event, link, commitPosition);
	}

	public static ResolvedEvent ForFailedResolvedLink(EventRecord link, ReadEventResult resolveResult,
		long? commitPosition = null) {
		return new ResolvedEvent(null, link, commitPosition, resolveResult);
	}

	public ResolvedEvent WithoutPosition() {
		return new ResolvedEvent(Event, Link, null, ResolveResult);
	}

	private TFPos? CalculatePosition(EventRecord @event) {
		if (@event is null)
			return null;

		// if this is the original event and we know where it was committed
		if (@event == OriginalEvent && _originalEventCommitPosition.HasValue)
			return new TFPos(_originalEventCommitPosition.Value, @event.LogPosition);

		// we don't know where this event was committed, unless it committed itself
		if (@event.IsSelfCommitted)
			return new TFPos(@event.LogPosition, @event.LogPosition);

		// we don't know where this event was committed
		return null;
	}

	public bool Equals(ResolvedEvent other) =>
		Equals(Event, other.Event) && Equals(Link, other.Link) &&
		_originalEventCommitPosition == other._originalEventCommitPosition &&
		ResolveResult == other.ResolveResult;

	public override bool Equals(object obj) =>
		obj is ResolvedEvent other && Equals(other);

	public override int GetHashCode() =>
		HashCode.Combine(Event, Link, _originalEventCommitPosition, (int)ResolveResult);

	public static bool operator ==(ResolvedEvent left, ResolvedEvent right) => left.Equals(right);

	public static bool operator !=(ResolvedEvent left, ResolvedEvent right) => !left.Equals(right);
}
