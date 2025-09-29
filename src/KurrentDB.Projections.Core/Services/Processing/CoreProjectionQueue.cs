// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.WorkItems;

namespace KurrentDB.Projections.Core.Services.Processing;

public class CoreProjectionQueue(IPublisher publisher, int pendingEventsThreshold, bool orderedPartitionProcessing) {
	private readonly StagedProcessingQueue _queuePendingEvents = new(
	[
		true /* record event order - async with ordered output*/, true
		/* get state partition - ordered as it may change correlation id - sync */,
		false
		/* load foreach state - async- unordered completion*/,
		orderedPartitionProcessing
		/* process Js - unordered/ordered - inherently unordered/ordered completion*/,
		true
		/* write emits - ordered - async ordered completion*/,
		false /* complete item */
	]);

	private CheckpointTag _lastEnqueuedEventTag;
	private bool _justInitialized;
	private bool _subscriptionPaused;

	public event Action EnsureTickPending {
		add { _queuePendingEvents.EnsureTickPending += value; }
		remove { _queuePendingEvents.EnsureTickPending -= value; }
	}

	/* record event order - async with ordered output*/
	/* get state partition - ordered as it may change correlation id - sync */
	/* load foreach state - async- unordered completion*/
	/* process Js - unordered/ordered - inherently unordered/ordered completion*/
	/* write emits - ordered - async ordered completion*/
	/* complete item */

	public bool IsRunning { get; private set; }

	public bool ProcessEvent() {
		var processed = false;
		switch (_queuePendingEvents.Count) {
			case > 0:
				processed = ProcessOneEventBatch();
				break;
			case 0 when _subscriptionPaused && !_unsubscribed:
				ResumeSubscription();
				break;
		}

		return processed;
	}

	public int GetBufferedEventCount() => _queuePendingEvents.Count;

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

	public string GetStatus() => _subscriptionPaused ? "/Paused" : "";

	private void ValidateQueueingOrder(CheckpointTag eventTag, bool allowCurrentPosition = false) {
		if (eventTag < _lastEnqueuedEventTag ||
			(!(allowCurrentPosition || _justInitialized) && eventTag <= _lastEnqueuedEventTag))
			throw new InvalidOperationException(
				$"Invalid order.  Last known tag is: '{_lastEnqueuedEventTag}'.  Current tag is: '{eventTag}'");
		_justInitialized = _justInitialized && (eventTag == _lastEnqueuedEventTag);
		_lastEnqueuedEventTag = eventTag;
	}

	private void PauseSubscription() {
		if (_subscriptionId == Guid.Empty)
			throw new InvalidOperationException("Not subscribed");
		if (!_subscriptionPaused && !_unsubscribed) {
			_subscriptionPaused = true;
			publisher.Publish(new ReaderSubscriptionManagement.Pause(_subscriptionId));
		}
	}

	private void ResumeSubscription() {
		if (_subscriptionId == Guid.Empty)
			throw new InvalidOperationException("Not subscribed");
		if (_subscriptionPaused && !_unsubscribed) {
			_subscriptionPaused = false;
			publisher.Publish(new ReaderSubscriptionManagement.Resume(_subscriptionId));
		}
	}

	private bool _unsubscribed;
	private Guid _subscriptionId;

	private bool ProcessOneEventBatch() {
		if (_queuePendingEvents.Count > pendingEventsThreshold)
			PauseSubscription();
		var processed = _queuePendingEvents.Process(max: 30);
		if (_subscriptionPaused && _queuePendingEvents.Count < pendingEventsThreshold / 2)
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
		IsRunning = isRunning;
	}
}
