// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;

namespace EventStore.Core.Services.PersistentSubscription;

public class StreamBuffer {
	private readonly int _maxBufferSize;
	private readonly IPersistentSubscriptionStreamPosition _initialSequence;
	private readonly LinkedList<OutstandingMessage> _retry = new LinkedList<OutstandingMessage>();
	private readonly LinkedList<OutstandingMessage> _buffer = new LinkedList<OutstandingMessage>();

	private readonly BoundedQueue<OutstandingMessage> _liveBuffer;

	public long LiveBufferCount {
		get { return _liveBuffer.Count; }
	}

	public int BufferCount {
		get { return _retry.Count + _buffer.Count; }
	}

	public int RetryBufferCount {
		get { return _retry.Count; }
	}

	public int ReadBufferCount {
		get { return _buffer.Count; }
	}

	public bool Live { get; private set; }

	public bool CanAccept(int count) {
		return _maxBufferSize - BufferCount > count;
	}

	public StreamBuffer(int maxBufferSize, int maxLiveBufferSize, IPersistentSubscriptionStreamPosition initialSequence, bool startInHistory) {
		Live = !startInHistory;
		_initialSequence = initialSequence;
		_maxBufferSize = maxBufferSize;
		_liveBuffer = new BoundedQueue<OutstandingMessage>(maxLiveBufferSize);
	}

	private void SwitchToLive() {
		while (_liveBuffer.Count > 0) {
			_buffer.AddLast(_liveBuffer.Dequeue());
		}

		Live = true;
	}

	private void DrainLiveTo(IPersistentSubscriptionStreamPosition eventPosition) {
		while (_liveBuffer.Count > 0 && _liveBuffer.Peek().EventPosition.CompareTo(eventPosition) < 0) {
			_liveBuffer.Dequeue();
		}
	}

	public void AddRetry(OutstandingMessage ev) {
		//add parked messages at the end of the list
		if (ev.IsReplayedEvent) {
			_retry.AddLast(ev);
			return;
		}

		//if it's not a parked message, it should have an event position
		Debug.Assert(ev.EventPosition != null);

		// Insert the retried event before any events with higher version number.
		var retryEventPosition = ev.EventPosition;

		var currentNode = _retry.First;

		while (currentNode != null) {
			var currentEventPosition = currentNode.Value.EventPosition;
			if (currentEventPosition is not null && retryEventPosition.CompareTo(currentEventPosition) < 0) {
				_retry.AddBefore(currentNode, ev);
				return;
			}

			currentNode = currentNode.Next;
		}

		_retry.AddLast(ev);
	}

	public void AddLiveMessage(OutstandingMessage ev) {
		if (Live) {
			if (_buffer.Count < _maxBufferSize)
				_buffer.AddLast(ev);
			else
				Live = false;
		}

		_liveBuffer.Enqueue(ev);
	}

	public void AddReadMessage(OutstandingMessage ev) {
		if (Live) return;
		if (_initialSequence != null &&
		    ev.EventPosition.CompareTo(_initialSequence) <= 0)
			return;

		var livePosition = TryPeekLive();
		if (livePosition == null || ev.EventPosition.CompareTo(livePosition) < 0) {
			_buffer.AddLast(ev);
		} else if (livePosition.CompareTo(ev.EventPosition) < 0) {
			DrainLiveTo(ev.EventPosition);
			SwitchToLive();
		} else {
			SwitchToLive();
		}
	}

	private IPersistentSubscriptionStreamPosition TryPeekLive() {
		return _liveBuffer.Count == 0 ? null : _liveBuffer.Peek().EventPosition;
	}

	public IEnumerable<OutstandingMessagePointer> Scan() {
		// This enumerator assumes that nothing is added to the buffers during enumeration.

		foreach (var list in new[] {_retry, _buffer}) // save on code duplication
		{
			var current = list.First;
			if (current != null) {
				do {
					// We have to copy next before yielding as the expectation is
					// that current is removed from the list setting next to null.
					var next = current.Next;

					yield return new OutstandingMessagePointer(current);

					current = next;
				} while (current != null);
			}
		}
	}

	public bool TryMoveToLive() {
		if (_liveBuffer.Count == 0) {
			Live = true;
			return true;
		}

		return false;
	}

	public (OutstandingMessage? message, long sequenceNumber) GetLowestRetry() {
		(OutstandingMessage? message, long sequenceNumber) result = (null, long.MaxValue);
		foreach(var x in _retry) {
			if (x.IsReplayedEvent || !x.EventSequenceNumber.HasValue) continue;
			if (x.EventSequenceNumber.Value < result.sequenceNumber) {
				result = (x, x.EventSequenceNumber.Value);
			}
		}
		return result;
	}

	public struct OutstandingMessagePointer {
		private readonly LinkedListNode<OutstandingMessage> _entry;

		internal OutstandingMessagePointer(LinkedListNode<OutstandingMessage> entry)
			: this() {
			_entry = entry;
		}

		public OutstandingMessage Message {
			get { return _entry.Value; }
		}

		public void MarkSent() {
			if (_entry.List == null) {
				throw new InvalidOperationException("The message can only be accepted once.");
			}

			_entry.List.Remove(_entry);
		}
	}
}

public enum BufferedStreamReaderState {
	Unknown,
	CatchingUp,
	Live
}
