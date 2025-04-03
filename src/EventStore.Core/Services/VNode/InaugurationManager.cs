// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.TransactionLog.Checkpoint;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.VNode;

public class InaugurationManager :
	IHandle<SystemMessage.StateChangeMessage>,
	IHandle<SystemMessage.ChaserCaughtUp>,
	IHandle<SystemMessage.EpochWritten>,
	IHandle<SystemMessage.CheckInaugurationConditions>,
	IHandle<ElectionMessage.ElectionsDone>,
	IHandle<ReplicationTrackingMessage.ReplicatedTo>,
	IHandle<ReplicationTrackingMessage.IndexedTo> {

	private readonly ILogger _log = Serilog.Log.ForContext<InaugurationManager>();
	private readonly IPublisher _publisher;
	private readonly IReadOnlyCheckpoint _replicationCheckpoint;
	private readonly IReadOnlyCheckpoint _indexCheckpoint;
	private readonly Message _scheduleCheckInaugurationConditions;
	private readonly IInaugurationStatusTracker _statusTracker;

	private VNodeState _nodeState;
	private ManagerState _managerState;
	private Guid _stateCorrelationId;
	private int _currentEpochNumber;
	private long _replicationCheckpointTarget;
	private long _indexCheckpointTarget;

	private ManagerState State {
		get => _managerState;
		set {
			_managerState = value;
			_statusTracker.OnStateChange(value);
		}
	}

	public enum ManagerState {
		Idle,
		WaitingForChaser,
		WritingEpoch,
		WaitingForConditions,
		BecomingLeader,
	}

	public InaugurationManager(
		IPublisher publisher,
		IReadOnlyCheckpoint replicationCheckpoint,
		IReadOnlyCheckpoint indexCheckpoint,
		IInaugurationStatusTracker statusTracker) {

		_log.Information("Using {name}", nameof(InaugurationManager));
		_publisher = publisher;
		_replicationCheckpoint = replicationCheckpoint;
		_indexCheckpoint = indexCheckpoint;
		_statusTracker = statusTracker;

		_scheduleCheckInaugurationConditions = TimerMessage.Schedule.Create(
			triggerAfter: TimeSpan.FromSeconds(1),
			envelope: _publisher,
			replyMessage: new SystemMessage.CheckInaugurationConditions());
	}

	public void Handle(ElectionMessage.ElectionsDone received) {
		_currentEpochNumber = received.ProposalNumber;
		Received(received, "currentEpochNumber {currentEpochNumber}.", _currentEpochNumber);
	}

	public void Handle(SystemMessage.StateChangeMessage received) {
		if (received is SystemMessage.BecomePreLeader becomePreLeader) {
			HandleBecomePreLeader(becomePreLeader);
		} else {
			HandleBecomeOtherNodeState(received);
		}
	}

	private void HandleBecomePreLeader(SystemMessage.BecomePreLeader received) {
		Received(
			received,
			"Starting inauguration process with correlation id {currentCorrelationId}. Waiting for chaser.",
			received.CorrelationId);
		_nodeState = received.State;
		State = ManagerState.WaitingForChaser;
		_stateCorrelationId = received.CorrelationId;
	}

	private void HandleBecomeOtherNodeState(SystemMessage.StateChangeMessage received) {
		if (State != ManagerState.Idle) {
			Received(received, "Inauguration process is now stopped.");
			State = ManagerState.Idle;
		}

		_nodeState = received.State;
	}

	public void Handle(SystemMessage.ChaserCaughtUp received) {
		if (State != ManagerState.WaitingForChaser) {
			// will get this in prereplica and prereadonly replica
			Ignore(received, "Not waiting for chaser.");
		} else if (received.CorrelationId != _stateCorrelationId) {
			Ignore(
				received,
				"Current correlation id is {currentCorrelationId} but received {receivedCorrelationId}.",
				_stateCorrelationId,
				received.CorrelationId);
		} else {
			Respond(
				received,
				new SystemMessage.WriteEpoch(_currentEpochNumber),
				"currentEpochNumber {currentEpochNumber}.",
				_currentEpochNumber);
			State = ManagerState.WritingEpoch;
		}
	}

	public void Handle(SystemMessage.EpochWritten received) {
		if (State != ManagerState.WritingEpoch) {
			Ignore(received, "Not writing epoch.");
		} else if (received.Epoch.EpochNumber != _currentEpochNumber) {
			Ignore(
				received,
				"Current epoch number is {currentEpochNumber} but received {receivedEpochNumber}.",
				_currentEpochNumber,
				received.Epoch.EpochNumber);
		} else {
			Respond(received, new SystemMessage.EnablePreLeaderReplication());
			State = ManagerState.WaitingForConditions;

			// want to replicate past the start of the epoch we just wrote.
			_replicationCheckpointTarget = received.Epoch.EpochPosition + 1;

			// and index past it too, to the $epoch-information event that immediately follows it
			_indexCheckpointTarget = received.Epoch.EpochPosition + 1;

			ConsiderBecomingLeader(received, log: true);
			_publisher.Publish(_scheduleCheckInaugurationConditions);
		}
	}

	public void Handle(ReplicationTrackingMessage.ReplicatedTo received) {
		if (State != ManagerState.WaitingForConditions) {
			// silently ignore. we'll get these all the time
		} else {
			ConsiderBecomingLeader(received, log: false);
		}
	}

	public void Handle(ReplicationTrackingMessage.IndexedTo received) {
		if (State != ManagerState.WaitingForConditions) {
			// silently ignore. we'll get these all the time
		} else {
			ConsiderBecomingLeader(received, log: false);
		}
	}

	public void Handle(SystemMessage.CheckInaugurationConditions received) {
		if (State != ManagerState.WaitingForConditions) {
			Ignore(received, "Not waiting for conditions.");
		} else {
			Respond(received, _scheduleCheckInaugurationConditions);
			ConsiderBecomingLeader(received, log: true);
		}
	}

	private void ConsiderBecomingLeader(Message received, bool log) {
		var replicationCurrent = _replicationCheckpoint.Read();
		var indexCurrent = _indexCheckpoint.Read();
		var replicationDone = replicationCurrent >= _replicationCheckpointTarget;
		var indexDone = indexCurrent >= _indexCheckpointTarget;

		if (log || (replicationDone && indexDone)) {
			LogExtraInformation(
				"Replication {progress}: {current:N0}/{target:N0}. Remaining: {remaining:N0}.",
				replicationDone ? "DONE" : "IN PROGRESS",
				replicationCurrent,
				_replicationCheckpointTarget,
				Math.Max(0, _replicationCheckpointTarget - replicationCurrent));

			LogExtraInformation(
				"Index {progress}: {current:N0}/{target:N0}. Remaining: {remaining:N0}.",
				indexDone ? "DONE" : "IN PROGRESS",
				indexCurrent,
				_indexCheckpointTarget,
				Math.Max(0, _indexCheckpointTarget - indexCurrent));
		}

		if (replicationDone && indexDone) {
			// transition to leader!
			Respond(
				received,
				new SystemMessage.BecomeLeader(_stateCorrelationId),
				"Correlation id {currentCorrelationId}",
				_stateCorrelationId);
			State = ManagerState.Idle;
		}
	}

	private void Received(Message received, string template = null, params object[] templateArgs) {
		LogExtraInformation(
			Combine(template, "RECEIVED {received}."),
			Combine(templateArgs, received.GetType().Name));
	}

	private void Respond(Message received, Message send, string template = null, params object[] templateArgs) {
		LogExtraInformation(
			Combine(template, "RECEIVED {received}. RESPONDING with {send}."),
			Combine(templateArgs, received.GetType().Name, send.GetType().Name));
		_publisher.Publish(send);
	}

	private void Ignore(Message received, string template = null, params object[] templateArgs) {
		LogExtraInformation(
			Combine(template, "IGNORING {received}."),
			Combine(templateArgs, received.GetType().Name));
	}

	private void LogExtraInformation(string template = null, params object[] templateArgs) {
		_log.Information(
			Combine(template, "{name} in state ({nodeState}, {managerState}):"),
			Combine(templateArgs, nameof(InaugurationManager), _nodeState, State));
	}

	private static string Combine(string template2, string template1) {
		if (template1 is null)
			return template2;

		if (template2 is null)
			return template1;

		return $"{template1} {template2}";
	}

	private static object[] Combine(object[] args2, params object[] args1) {
		if (args1 is null)
			return args2;

		if (args2 is null)
			return args1;

		return args1.Concat(args2).ToArray();
	}
}
