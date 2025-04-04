// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.WorkItems;

namespace EventStore.Projections.Core.Services.Processing;

public class CoreProjectionQueue {
	private readonly StagedProcessingQueue _queuePendingEvents;

	private readonly IPublisher _publisher;
	private readonly int _pendingEventsThreshold;

	private CheckpointTag _lastEnqueuedEventTag;
	private bool _justInitialized;
	private bool _subscriptionPaused;

	public event Action EnsureTickPending {
		add { _queuePendingEvents.EnsureTickPending += value; }
		remove { _queuePendingEvents.EnsureTickPending -= value; }
	}

	public CoreProjectionQueue(IPublisher publisher, int pendingEventsThreshold, bool orderedPartitionProcessing) {
		_queuePendingEvents =
			new StagedProcessingQueue(
				new[] {
					true /* record event order - async with ordered output*/, true
					/* get state partition - ordered as it may change correlation id - sync */,
					false
					/* load foreach state - async- unordered completion*/,
					orderedPartitionProcessing
					/* process Js - unordered/ordered - inherently unordered/ordered completion*/,
					true
					/* write emits - ordered - async ordered completion*/,
					false /* complete item */
				});
		_publisher = publisher;
		_pendingEventsThreshold = pendingEventsThreshold;
	}

	public bool IsRunning {
		get { return _isRunning; }
	}

	public bool ProcessEvent() {
		var processed = false;
		if (_queuePendingEvents.Count > 0) {
			processed = ProcessOneEventBatch();
		} else if (_queuePendingEvents.Count == 0 && _subscriptionPaused && !_unsubscribed) {
			ResumeSubscription();
		}

		return processed;
	}

	public int GetBufferedEventCount() {
		return _queuePendingEvents.Count;
	}

	public void EnqueueTask(WorkItem workItem, CheckpointTag workItemCheckpointTag,
		bool allowCurrentPosition = false) {
		ValidateQueueingOrder(workItemCheckpointTag, allowCurrentPosition);
		workItem.SetProjectionQueue(this);
		workItem.SetCheckpointTag(workItemCheckpointTag);
		_queuePendingEvents.Enqueue(workItem);
	}

	public void EnqueueOutOfOrderTask(WorkItem workItem) {
		if (_lastEnqueuedEventTag == null)
			throw new InvalidOperationException(
				"Cannot enqueue an out-of-order task.  The projection position is currently unknown.");
		workItem.SetProjectionQueue(this);
		workItem.SetCheckpointTag(_lastEnqueuedEventTag);
		_queuePendingEvents.Enqueue(workItem);
	}

	public void InitializeQueue(CheckpointTag startingPosition) {
		_subscriptionPaused = false;
		_unsubscribed = false;
		_subscriptionId = Guid.Empty;

		_queuePendingEvents.Initialize();

		_lastEnqueuedEventTag = startingPosition;
		_justInitialized = true;
	}

	public string GetStatus() {
		return (_subscriptionPaused ? "/Paused" : "");
	}

	private void ValidateQueueingOrder(CheckpointTag eventTag, bool allowCurrentPosition = false) {
		if (eventTag < _lastEnqueuedEventTag ||
			(!(allowCurrentPosition || _justInitialized) && eventTag <= _lastEnqueuedEventTag))
			throw new InvalidOperationException(
				string.Format(
					"Invalid order.  Last known tag is: '{0}'.  Current tag is: '{1}'", _lastEnqueuedEventTag,
					eventTag));
		_justInitialized = _justInitialized && (eventTag == _lastEnqueuedEventTag);
		_lastEnqueuedEventTag = eventTag;
	}

	private void PauseSubscription() {
		if (_subscriptionId == Guid.Empty)
			throw new InvalidOperationException("Not subscribed");
		if (!_subscriptionPaused && !_unsubscribed) {
			_subscriptionPaused = true;
			_publisher.Publish(
				new ReaderSubscriptionManagement.Pause(_subscriptionId));
		}
	}

	private void ResumeSubscription() {
		if (_subscriptionId == Guid.Empty)
			throw new InvalidOperationException("Not subscribed");
		if (_subscriptionPaused && !_unsubscribed) {
			_subscriptionPaused = false;
			_publisher.Publish(
				new ReaderSubscriptionManagement.Resume(_subscriptionId));
		}
	}

	private bool _unsubscribed;
	private Guid _subscriptionId;
	private bool _isRunning;

	private bool ProcessOneEventBatch() {
		if (_queuePendingEvents.Count > _pendingEventsThreshold)
			PauseSubscription();
		var processed = _queuePendingEvents.Process(max: 30);
		if (_subscriptionPaused && _queuePendingEvents.Count < _pendingEventsThreshold / 2)
			ResumeSubscription();

		return processed;
	}

	public void Unsubscribed() {
		_unsubscribed = true;
	}

	public void Subscribed(Guid currentSubscriptionId) {
		if (_unsubscribed)
			throw new InvalidOperationException("Unsubscribed");
		if (_subscriptionId != Guid.Empty)
			throw new InvalidOperationException("Already subscribed");
		_subscriptionId = currentSubscriptionId;
	}

	public void SetIsRunning(bool isRunning) {
		_isRunning = isRunning;
	}
}
