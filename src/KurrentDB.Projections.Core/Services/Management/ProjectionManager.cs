// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using KurrentDB.Common.Options;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Projections.Core.Common;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Standard;
using Serilog;
using ILogger = Serilog.ILogger;
using OperationResult = KurrentDB.Core.Messages.OperationResult;
using TelemetryMessage = KurrentDB.Core.Telemetry.TelemetryMessage;

namespace KurrentDB.Projections.Core.Services.Management;

public class ProjectionManager
	: IDisposable,
		IHandle<ClientMessage.ReadStreamEventsBackwardCompleted>,
		IHandle<ClientMessage.ReadStreamEventsForwardCompleted>,
		IHandle<ClientMessage.WriteEventsCompleted>,
		IHandle<ClientMessage.DeleteStreamCompleted>,
		IHandle<ProjectionManagementMessage.Command.Post>,
		IHandle<ProjectionManagementMessage.Command.PostBatch>,
		IHandle<ProjectionManagementMessage.Command.UpdateQuery>,
		IHandle<ProjectionManagementMessage.Command.GetQuery>,
		IHandle<ProjectionManagementMessage.Command.Delete>,
		IHandle<ProjectionManagementMessage.Command.GetStatistics>,
		IHandle<ProjectionManagementMessage.Command.GetState>,
		IHandle<ProjectionManagementMessage.Command.GetResult>,
		IHandle<ProjectionManagementMessage.Command.Disable>,
		IHandle<ProjectionManagementMessage.Command.Enable>,
		IHandle<ProjectionManagementMessage.Command.Abort>,
		IHandle<ProjectionManagementMessage.Command.SetRunAs>,
		IHandle<ProjectionManagementMessage.Command.Reset>,
		IHandle<ProjectionManagementMessage.Command.GetConfig>,
		IHandle<ProjectionManagementMessage.Command.UpdateConfig>,
		IHandle<ProjectionSubsystemMessage.StartComponents>,
		IHandle<ProjectionSubsystemMessage.StopComponents>,
		IHandle<ProjectionManagementMessage.Internal.CleanupExpired>,
		IHandle<ProjectionManagementMessage.Internal.Deleted>,
		IHandle<CoreProjectionStatusMessage.Started>,
		IHandle<CoreProjectionStatusMessage.Stopped>,
		IHandle<CoreProjectionStatusMessage.Faulted>,
		IHandle<CoreProjectionStatusMessage.Prepared>,
		IHandle<CoreProjectionStatusMessage.StateReport>,
		IHandle<CoreProjectionStatusMessage.ResultReport>,
		IHandle<CoreProjectionStatusMessage.StatisticsReport>,
		IHandle<TelemetryMessage.Request> {
	public const int ProjectionQueryId = -2;
	public const int ProjectionCreationRetryCount = 1;
	public const string ServiceName = "ProjectionManager";

	private readonly ILogger _logger = Log.ForContext<ProjectionManager>();

	private readonly IPublisher _inputQueue;
	private readonly IPublisher _publisher;
	private readonly Tuple<Guid, IPublisher>[] _queues;
	private readonly Guid[] _workers;
	private readonly TimeSpan _projectionsQueryExpiry;

	private readonly ITimeProvider _timeProvider;
	private readonly ProjectionType _runProjections;
	private readonly bool _initializeSystemProjections;
	private readonly Dictionary<string, ManagedProjection> _projections;
	private readonly Dictionary<Guid, string> _projectionsMap;

	private readonly RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted>
		_writeDispatcher;

	private readonly RequestResponseDispatcher<ClientMessage.DeleteStream, ClientMessage.DeleteStreamCompleted>
		_streamDispatcher;

	private readonly
		RequestResponseDispatcher
		<ClientMessage.ReadStreamEventsForward, ClientMessage.ReadStreamEventsForwardCompleted>
		_readForwardDispatcher;

	private readonly ReadDispatcher _readDispatcher;

	private int _readEventsBatchSize = 100;

	private int _lastUsedQueue = 0;
	private bool _started;
	private bool _projectionsStarted;
	private long _projectionsRegistrationExpectedVersion = 0;
	private bool _isWritePending = false;
	private HashSet<string> _projectionsRegistrationState = new HashSet<string>();
	private readonly IEnvelope _publishEnvelope;

	private readonly
		RequestResponseDispatcher<CoreProjectionManagementMessage.GetState, CoreProjectionStatusMessage.StateReport>
		_getStateDispatcher;

	private readonly
		RequestResponseDispatcher
		<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>
		_getResultDispatcher;

	private readonly IODispatcher _ioDispatcher;

	private Guid _instanceCorrelationId = Guid.Empty;
	private IProjectionTracker _projectionTracker;
	private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(2000);
	private readonly TimerMessage.Schedule _getStats;

	public ProjectionManager(
		IPublisher inputQueue,
		IPublisher publisher,
		IDictionary<Guid, IPublisher> queueMap,
		ITimeProvider timeProvider,
		ProjectionType runProjections,
		IODispatcher ioDispatcher,
		TimeSpan projectionQueryExpiry,
		IProjectionTracker projectionTracker,
		bool initializeSystemProjections = true) {
		if (inputQueue == null)
			throw new ArgumentNullException("inputQueue");
		if (publisher == null)
			throw new ArgumentNullException("publisher");
		if (queueMap == null)
			throw new ArgumentNullException("queueMap");
		if (queueMap.Count == 0)
			throw new ArgumentException("At least one queue is required", "queueMap");

		_inputQueue = inputQueue;
		_publisher = publisher;
		_queues = queueMap.Select(v => Tuple.Create(v.Key, v.Value)).ToArray();
		_workers = _queues.Select(v => v.Item1).ToArray();

		_timeProvider = timeProvider;
		_runProjections = runProjections;
		_initializeSystemProjections = initializeSystemProjections;
		_ioDispatcher = ioDispatcher;
		_projectionsQueryExpiry = projectionQueryExpiry;
		_projectionTracker = projectionTracker;

		_writeDispatcher =
			new RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted>(
				publisher,
				v => v.CorrelationId,
				v => v.CorrelationId,
				_inputQueue);
		_readDispatcher = new ReadDispatcher(
			publisher,
			v => v.CorrelationId,
			v => v.CorrelationId,
			v => v.CorrelationId,
			_inputQueue);
		_readForwardDispatcher =
			new RequestResponseDispatcher
				<ClientMessage.ReadStreamEventsForward, ClientMessage.ReadStreamEventsForwardCompleted>(
					publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_inputQueue);
		_streamDispatcher =
			new RequestResponseDispatcher<ClientMessage.DeleteStream, ClientMessage.DeleteStreamCompleted>(
				publisher,
				v => v.CorrelationId,
				v => v.CorrelationId,
				_inputQueue);

		_projections = new Dictionary<string, ManagedProjection>();
		_projectionsMap = new Dictionary<Guid, string>();
		_publishEnvelope = _inputQueue;
		_getStateDispatcher =
			new RequestResponseDispatcher
				<CoreProjectionManagementMessage.GetState, CoreProjectionStatusMessage.StateReport>(
					_publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_inputQueue);
		_getResultDispatcher =
			new RequestResponseDispatcher
				<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>(
					_publisher,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_inputQueue);
		_getStats = TimerMessage.Schedule.Create(_interval, _inputQueue,
			new ProjectionManagementMessage.Command.GetStatistics(new CallbackEnvelope(PushStatsToProjectionTracker), ProjectionMode.AllNonTransient, null, true));
	}

	private void PushStatsToProjectionTracker(Message message) {
		if (message is ProjectionManagementMessage.Statistics stats) {
			_projectionTracker.OnNewStats(stats.Projections);
		}
		_publisher.Publish(_getStats);
	}

	public void Handle(ProjectionSubsystemMessage.StartComponents message) {
		if (_started) {
			_logger.Debug("PROJECTIONS: Projection manager already started. Correlation: {correlation}",
				message.InstanceCorrelationId);
			return;
		}

		_instanceCorrelationId = message.InstanceCorrelationId;
		_logger.Debug("PROJECTIONS: Starting Projections Manager. Correlation: {correlation}", _instanceCorrelationId);

		_started = true;
		if (_runProjections >= ProjectionType.System)
			StartExistingProjections(
				() => {
					_projectionsStarted = true;
					_publisher.Publish(_getStats);
					ScheduleExpire();
				});
		_publisher.Publish(new ProjectionSubsystemMessage.ComponentStarted(ServiceName, _instanceCorrelationId));
	}

	public void Handle(ProjectionSubsystemMessage.StopComponents message) {
		if (!_started) {
			_logger.Debug("PROJECTIONS: Projection manager already stopped. Correlation: {correlation}",
				message.InstanceCorrelationId);
			return;
		}

		if (_instanceCorrelationId != message.InstanceCorrelationId) {
			_logger.Debug("PROJECTIONS: Projection Manager received stop request for incorrect correlation id." +
						  "Current: {correlationId}. Requested: {requestedCorrelationId}", _instanceCorrelationId, message.InstanceCorrelationId);
			return;
		}
		_logger.Debug("PROJECTIONS: Stopping Projections Manager. Correlation {correlation}", _instanceCorrelationId);
		Stop();
	}

	private void ScheduleExpire() {
		if (!_projectionsStarted)
			return;
		_publisher.Publish(
			TimerMessage.Schedule.Create(
				TimeSpan.FromSeconds(60),
				_publishEnvelope,
				new ProjectionManagementMessage.Internal.CleanupExpired()));
	}

	private void Stop() {
		_started = false;
		_projectionsStarted = false;
		_ioDispatcher.StartDraining(() =>
			_publisher.Publish(new ProjectionSubsystemMessage.IODispatcherDrained(ServiceName)));

		_projections.Clear();
		_projectionsMap.Clear();

		_publisher.Publish(new ProjectionSubsystemMessage.ComponentStopped(ServiceName, _instanceCorrelationId));
	}

	public void Handle(ProjectionManagementMessage.Command.Post message) {
		if (!_projectionsStarted)
			return;

		if (message.Mode == ProjectionMode.Transient) {
			var transientProjection = new PendingProjection(ProjectionQueryId, message);
			if (!ValidateProjections(new[] { transientProjection }, message))
				return;

			PostNewTransientProjection(transientProjection, message.Envelope);
		} else {
			if (_isWritePending) {
				DelayMessage(message);
			} else {
				var expectedVersion = _projectionsRegistrationExpectedVersion;
				var pendingProjections = new Dictionary<string, PendingProjection> {
					{message.Name, new PendingProjection(expectedVersion + 1, message)}
				};
				if (!ValidateProjections(pendingProjections.Values.ToArray(), message))
					return;

				PostNewProjections(pendingProjections, expectedVersion, message.Envelope);
			}
		}
	}

	public void Handle(ProjectionManagementMessage.Command.PostBatch message) {
		if (!_projectionsStarted || !message.Projections.Any())
			return;

		if (message.Projections.Any(p => p.Mode == ProjectionMode.Transient)) {
			message.Envelope.ReplyWith(
				new ProjectionManagementMessage.OperationFailed(
					"Transient projections in batches are not supported."));
			return;
		}
		if (_isWritePending) {
			DelayMessage(message);
		} else {
			var expectedVersion = _projectionsRegistrationExpectedVersion;
			var pendingProjections = new Dictionary<string, PendingProjection>();

			var projectionId = expectedVersion + 1;
			foreach (var projection in message.Projections) {
				pendingProjections.Add(projection.Name, new PendingProjection(projectionId, projection));
				projectionId++;
			}

			if (!ValidateProjections(pendingProjections.Values.ToArray(), message))
				return;

			PostNewProjections(pendingProjections, expectedVersion, message.Envelope);
		}
	}

	private bool ValidateProjections(
		PendingProjection[] projections,
		ProjectionManagementMessage.Command.ControlMessage message) {
		var duplicateNames = new List<string>();

		foreach (var projection in projections) {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(
					projection.Mode,
					ReadWrite.Write,
					null,
					message,
					replace: projection.EnableRunAs)) {

				_logger.Information("PROJECTIONS: Projections batch rejected due to invalid RunAs");
				message.Envelope.ReplyWith(
					new ProjectionManagementMessage.OperationFailed("Invalid RunAs"));
				return false;
			}

			if (string.IsNullOrWhiteSpace(projection.Name)) {
				message.Envelope.ReplyWith(
					new ProjectionManagementMessage.OperationFailed("Projection name is required"));
				return false;
			}

			if (_projectionsRegistrationState.Contains(projection.Name)) {
				duplicateNames.Add(projection.Name);
			}
		}

		if (duplicateNames.Any()) {
			var duplicatesMsg = $"Duplicate projection names : {string.Join(", ", duplicateNames)}";
			_logger.Debug($"PROJECTIONS: Conflict. {duplicatesMsg}");
			message.Envelope.ReplyWith(
				new ProjectionManagementMessage.Conflict(duplicatesMsg));
			return false;
		}

		return true;
	}

	public void Handle(ProjectionManagementMessage.Command.Delete message) {
		if (!_projectionsStarted)
			return;
		var projection = GetProjection(message.Name);
		if (projection == null) {
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
			return;
		}

		if (IsSystemProjection(message.Name)) {
			message.Envelope.ReplyWith(
				new ProjectionManagementMessage.OperationFailed(
					"We currently don't allow for the deletion of System Projections."));
			return;
		}

		if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Write, projection.RunAs,
			message))
			return;
		try {
			projection.Handle(message);
		} catch (InvalidOperationException ex) {
			message.Envelope.ReplyWith(new ProjectionManagementMessage.OperationFailed(ex.Message));
		}
	}

	private bool IsSystemProjection(string name) {
		return name == ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection ||
			   name == ProjectionNamesBuilder.StandardProjections.EventByTypeStandardProjection ||
			   name == ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection ||
			   name == ProjectionNamesBuilder.StandardProjections.StreamsStandardProjection ||
			   name == ProjectionNamesBuilder.StandardProjections.EventByCorrIdStandardProjection;
	}

	public void Handle(ProjectionManagementMessage.Command.GetQuery message) {
		if (!_projectionsStarted)
			return;
		var projection = GetProjection(message.Name);
		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Read, projection.RunAs,
				message))
				return;
			projection.Handle(message);
		}
	}

	public void Handle(ProjectionManagementMessage.Command.UpdateQuery message) {
		if (!_projectionsStarted)
			return;
		_logger.Information(
			"Updating '{projection}' projection source to '{source}`",
			message.Name,
			message.Query);
		var projection = GetProjection(message.Name);

		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Write, projection.RunAs,
				message))
				return;
			projection.Handle(message); // update query text
		}
	}

	public void Handle(ProjectionManagementMessage.Command.Disable message) {
		if (!_projectionsStarted)
			return;
		_logger.Information("Disabling '{projection}' projection", message.Name);

		var projection = GetProjection(message.Name);
		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Write, projection.RunAs,
				message))
				return;
			projection.Handle(message);
		}
	}

	public void Handle(ProjectionManagementMessage.Command.Enable message) {
		if (!_projectionsStarted)
			return;
		_logger.Information("Enabling '{projection}' projection", message.Name);

		var projection = GetProjection(message.Name);
		if (projection == null) {
			_logger.Error("DBG: PROJECTION *{projection}* NOT FOUND.", message.Name);
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		} else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Write, projection.RunAs,
				message))
				return;
			projection.Handle(message);
		}
	}

	public void Handle(ProjectionManagementMessage.Command.Abort message) {
		if (!_projectionsStarted)
			return;
		_logger.Information("Aborting '{projection}' projection", message.Name);

		var projection = GetProjection(message.Name);
		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Write, projection.RunAs,
				message))
				return;
			projection.Handle(message);
		}
	}

	public void Handle(ProjectionManagementMessage.Command.SetRunAs message) {
		if (!_projectionsStarted)
			return;
		_logger.Information("Setting RunAs1 account for '{projection}' projection", message.Name);

		var projection = GetProjection(message.Name);
		if (projection == null) {
			_logger.Error("DBG: PROJECTION *{projection}* NOT FOUND.", message.Name);
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		} else {
			if (
				!ProjectionManagementMessage.RunAs.ValidateRunAs(
					projection.Mode, ReadWrite.Write, projection.RunAs, message,
					message.Action == ProjectionManagementMessage.Command.SetRunAs.SetRemove.Set))
				return;

			projection.Handle(message);
		}
	}

	public void Handle(ProjectionManagementMessage.Command.Reset message) {
		if (!_projectionsStarted)
			return;
		_logger.Information("Resetting '{projection}' projection", message.Name);

		var projection = GetProjection(message.Name);
		if (projection == null) {
			_logger.Error("DBG: PROJECTION *{projection}* NOT FOUND.", message.Name);
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		} else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Write, projection.RunAs,
				message))
				return;
			projection.Handle(message);
		}
	}

	public void Handle(ProjectionManagementMessage.Command.GetStatistics message) {
		if (!_projectionsStarted)
			return;
		if (!string.IsNullOrEmpty(message.Name)) {
			var projection = GetProjection(message.Name);
			if (projection == null)
				message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
			else
				message.Envelope.ReplyWith(
					new ProjectionManagementMessage.Statistics(new[] { projection.GetStatistics() }));

		} else {
			var statuses = (from projectionNameValue in _projections
							let projection = projectionNameValue.Value
							where !projection.Deleted
							where
								message.Mode == null || message.Mode == projection.Mode
													 || (message.Mode.GetValueOrDefault() == ProjectionMode.AllNonTransient
														 && projection.Mode != ProjectionMode.Transient)
							let status = projection.GetStatistics()
							select status).ToArray();
			message.Envelope.ReplyWith(new ProjectionManagementMessage.Statistics(statuses));
		}
	}

	public void Handle(ProjectionManagementMessage.Command.GetState message) {
		if (!_projectionsStarted)
			return;
		var projection = GetProjection(message.Name);
		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else
			projection.Handle(message);
	}

	public void Handle(ProjectionManagementMessage.Command.GetResult message) {
		if (!_projectionsStarted)
			return;
		var projection = GetProjection(message.Name);
		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else
			projection.Handle(message);
	}

	public void Handle(ProjectionManagementMessage.Command.GetConfig message) {
		if (!_projectionsStarted)
			return;
		var projection = GetProjection(message.Name);
		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Read, projection.RunAs,
				message))
				return;
			projection.Handle(message);
		}
	}

	public void Handle(ProjectionManagementMessage.Command.UpdateConfig message) {
		if (!_projectionsStarted)
			return;
		var projection = GetProjection(message.Name);
		if (projection == null)
			message.Envelope.ReplyWith(new ProjectionManagementMessage.NotFound());
		else {
			if (!ProjectionManagementMessage.RunAs.ValidateRunAs(projection.Mode, ReadWrite.Read, projection.RunAs,
				message))
				return;
			try {
				projection.Handle(message);
			} catch (InvalidOperationException ex) {
				message.Envelope.ReplyWith(new ProjectionManagementMessage.OperationFailed(ex.Message));
				return;
			}
		}
	}

	public void Handle(ProjectionManagementMessage.Internal.CleanupExpired message) {
		ScheduleExpire();
		CleanupExpired();
	}

	private void CleanupExpired() {
		foreach (var managedProjection in _projections.ToArray()) {
			managedProjection.Value.Handle(new ProjectionManagementMessage.Internal.CleanupExpired());
		}
	}

	public void Handle(CoreProjectionStatusMessage.Started message) {
		string name;
		if (_projectionsMap.TryGetValue(message.ProjectionId, out name)) {
			var projection = _projections[name];
			projection.Handle(message);
		}
	}

	public void Handle(CoreProjectionStatusMessage.Stopped message) {
		string name;
		if (_projectionsMap.TryGetValue(message.ProjectionId, out name)) {
			var projection = _projections[name];
			projection.Handle(message);
		}
	}

	public void Handle(CoreProjectionStatusMessage.Faulted message) {
		string name;
		if (_projectionsMap.TryGetValue(message.ProjectionId, out name)) {
			var projection = _projections[name];
			projection.Handle(message);
		}
	}

	public void Handle(CoreProjectionStatusMessage.Prepared message) {
		string name;
		if (_projectionsMap.TryGetValue(message.ProjectionId, out name)) {
			var projection = _projections[name];
			projection.Handle(message);
		}
	}

	public void Handle(CoreProjectionStatusMessage.StateReport message) {
		_getStateDispatcher.Handle(message);
	}

	public void Handle(CoreProjectionStatusMessage.ResultReport message) {
		_getResultDispatcher.Handle(message);
	}

	public void Handle(CoreProjectionStatusMessage.StatisticsReport message) {
		string name;
		if (_projectionsMap.TryGetValue(message.ProjectionId, out name)) {
			var projection = _projections[name];
			projection.Handle(message);
		}
	}

	public void Handle(ClientMessage.ReadStreamEventsBackwardCompleted message) {
		_readDispatcher.Handle(message);
	}

	public void Handle(ClientMessage.ReadStreamEventsForwardCompleted message) {
		_readForwardDispatcher.Handle(message);
	}

	public void Handle(ClientMessage.WriteEventsCompleted message) {
		_writeDispatcher.Handle(message);
	}

	public void Handle(ClientMessage.DeleteStreamCompleted message) {
		_streamDispatcher.Handle(message);
	}

	public void Handle(ProjectionManagementMessage.Internal.Deleted message) {
		var projection = GetProjection(message.Name);

		if (projection == null)
			return;

		if (projection.Mode == ProjectionMode.Transient) {
			// We don't need to write a delete, as transient projections don't write creations
			_projections.Remove(message.Name);
			_projectionsMap.Remove(message.Id);
			_projectionsRegistrationState.Remove(message.Name);
			return;
		}

		if (_isWritePending) {
			DelayMessage(message);
		} else {
			DeleteProjection(message,
				expVer => {
					_projections.Remove(message.Name);
					_projectionsMap.Remove(message.Id);
					_projectionsRegistrationState.Remove(message.Name);
					_projectionsRegistrationExpectedVersion = expVer;
				});
		}
	}

	private void DeleteProjection(
		ProjectionManagementMessage.Internal.Deleted message, Action<long> completed, int retryCount = ProjectionCreationRetryCount) {
		var corrId = Guid.NewGuid();
		var writeDelete = new ClientMessage.WriteEvents(
				corrId,
				corrId,
				_writeDispatcher.Envelope,
				true,
				ProjectionNamesBuilder.ProjectionsRegistrationStream,
				_projectionsRegistrationExpectedVersion,
				new Event(
					Guid.NewGuid(),
					ProjectionEventTypes.ProjectionDeleted,
					false,
					Helper.UTF8NoBom.GetBytes(message.Name),
					Empty.ByteArray, SchemaInfo.None, SchemaInfo.None),
				SystemAccounts.System);

		_isWritePending = true;
		_writeDispatcher.Publish(
			writeDelete,
			m => WriteProjectionDeletedCompleted(m, writeDelete, message, completed, retryCount));
	}

	public void Dispose() {
		foreach (var projection in _projections.Values)
			projection.Dispose();
		_projections.Clear();
	}

	private ManagedProjection GetProjection(string name) {
		ManagedProjection result;
		return _projections.TryGetValue(name, out result) ? result : null;
	}

	private void StartExistingProjections(Action completed) {
		var registeredProjections = new Dictionary<string, long>();
		ReadProjectionsList(
			registeredProjections,
			r => StartRegisteredProjections(r, completed));
	}

	private void ReadProjectionsList(
		IDictionary<string, long> registeredProjections,
		Action<IDictionary<string, long>> completedAction,
		long from = 0) {

		_logger.Debug("PROJECTIONS: Reading Existing Projections from {stream}", ProjectionNamesBuilder.ProjectionsRegistrationStream);
		var corrId = Guid.NewGuid();
		_readForwardDispatcher.Publish(
			new ClientMessage.ReadStreamEventsForward(
				corrId,
				corrId,
				_readDispatcher.Envelope,
				ProjectionNamesBuilder.ProjectionsRegistrationStream,
				from,
				_readEventsBatchSize,
				resolveLinkTos: false,
				requireLeader: false,
				validationStreamVersion: null,
				user: SystemAccounts.System,
				replyOnExpired: false,
				expires: DateTime.MaxValue),
			m => OnProjectionsListReadCompleted(m, registeredProjections, from, completedAction));
	}

	private void OnProjectionsListReadCompleted(
		ClientMessage.ReadStreamEventsForwardCompleted msg,
		IDictionary<string, long> registeredProjections,
		long requestedFrom,
		Action<IDictionary<string, long>> completedAction) {
		switch (msg.Result) {
			case ReadStreamResult.Success:
				foreach (var evnt in msg.Events) {
					var projectionId = evnt.Event.EventNumber;
					if (projectionId == 0)
						projectionId = Int32.MaxValue - 1;
					if (evnt.Event.EventType == ProjectionEventTypes.ProjectionsInitialized) {
						registeredProjections.Add(ProjectionEventTypes.ProjectionsInitialized, projectionId);
						continue;
					}

					var projectionName = Helper.UTF8NoBom.GetString(evnt.Event.Data.Span);
					if (string.IsNullOrEmpty(projectionName)
						|| _projections.ContainsKey(projectionName)) {
						_logger.Warning(
							"PROJECTIONS: The following projection: {projection} has a duplicate registration event.",
							projectionName);
						continue;
					}

					if (evnt.Event.EventType == ProjectionEventTypes.ProjectionCreated) {
						if (registeredProjections.ContainsKey(projectionName)) {
							registeredProjections[projectionName] = projectionId;
							_logger.Warning(
								"PROJECTIONS: The following projection: {projection} has a duplicate created event. Using projection Id {projectionId}",
								projectionName, projectionId);
							continue;
						}

						registeredProjections.Add(projectionName, projectionId);
					} else if (evnt.Event.EventType == ProjectionEventTypes.ProjectionDeleted) {
						registeredProjections.Remove(projectionName);
					}
				}
				_projectionsRegistrationExpectedVersion = msg.LastEventNumber;

				if (!msg.IsEndOfStream) {
					ReadProjectionsList(registeredProjections, completedAction, from: msg.NextEventNumber);
					return;
				}

				break;
			case ReadStreamResult.StreamDeleted:
			case ReadStreamResult.Error:
			case ReadStreamResult.AccessDenied:
				_logger.Fatal(
					"There was an error reading the projections list due to {e}. Projections could not be loaded.",
					msg.Result);
				return;
		}
		completedAction(registeredProjections);
	}

	private void StartRegisteredProjections(IDictionary<string, long> registeredProjections,
		Action completedAction) {
		if (!registeredProjections.Any()) {
			_logger.Debug("PROJECTIONS: No projections were found in {stream}, starting from empty stream",
				ProjectionNamesBuilder.ProjectionsRegistrationStream);
			WriteProjectionsInitialized(
				() => {
					completedAction();
					CreateSystemProjections();
				},
				Guid.NewGuid());
			return;
		}

		List<string> projections = registeredProjections
			.Where(x => x.Key != ProjectionEventTypes.ProjectionsInitialized)
			.Select(x => x.Key).ToList();

		_logger.Debug(
			"PROJECTIONS: Found the following projections in {stream}: {projections}",
			ProjectionNamesBuilder.ProjectionsRegistrationStream,
			projections);

		foreach (var projection in projections)
			_projectionsRegistrationState.Add(projection);

		//create any missing system projections
		CreateSystemProjections(registeredProjections.Select(x => x.Key).ToList());

		foreach (var projectionRegistration in registeredProjections.Where(x =>
			x.Key != ProjectionEventTypes.ProjectionsInitialized)) {
			int queueIndex = GetNextWorkerIndex();
			var managedProjection = CreateManagedProjectionInstance(
				projectionRegistration.Key,
				projectionRegistration.Value,
				Guid.NewGuid(),
				_workers[queueIndex]);
			managedProjection.InitializeExisting(projectionRegistration.Key);
		}

		completedAction();
	}

	private bool IsProjectionEnabledToRunByMode(string projectionName) {
		return _runProjections >= ProjectionType.All
			   || _runProjections == ProjectionType.System && projectionName.StartsWith("$");
	}

	private void WriteProjectionsInitialized(Action action, Guid registrationEventId) {
		var corrId = Guid.NewGuid();
		_writeDispatcher.Publish(
			new ClientMessage.WriteEvents(
				corrId,
				corrId,
				_writeDispatcher.Envelope,
				true,
				ProjectionNamesBuilder.ProjectionsRegistrationStream,
				ExpectedVersion.NoStream,
				new Event(registrationEventId, ProjectionEventTypes.ProjectionsInitialized, false, Empty.ByteArray,
					Empty.ByteArray, SchemaInfo.None, SchemaInfo.None),
				SystemAccounts.System),
			completed => WriteProjectionsInitializedCompleted(completed, registrationEventId, action));
	}

	private void WriteProjectionsInitializedCompleted(ClientMessage.WriteEventsCompleted completed,
		Guid registrationEventId, Action action) {
		switch (completed.Result) {
			case OperationResult.Success:
				action();
				break;
			case OperationResult.CommitTimeout:
			case OperationResult.ForwardTimeout:
			case OperationResult.PrepareTimeout:
				WriteProjectionsInitialized(action, registrationEventId);
				break;
			default:
				_logger.Fatal("Cannot initialize projections subsystem. Cannot write a fake projection");
				break;
		}
	}

	private void CreateSystemProjections() {
		CreateSystemProjections(new List<string>());
	}

	private void CreateSystemProjections(List<string> existingSystemProjections) {
		var systemProjections = new List<ProjectionManagementMessage.Command.PostBatch.ProjectionPost>();

		if (!_initializeSystemProjections) {
			return;
		}

		if (!existingSystemProjections.Contains(ProjectionNamesBuilder.StandardProjections
			.StreamsStandardProjection))
			systemProjections.Add(CreateSystemProjectionPost(
				ProjectionNamesBuilder.StandardProjections.StreamsStandardProjection,
				typeof(IndexStreams),
				""));

		if (!existingSystemProjections.Contains(ProjectionNamesBuilder.StandardProjections
			.StreamByCategoryStandardProjection))
			systemProjections.Add(CreateSystemProjectionPost(
				ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection,
				typeof(CategorizeStreamByPath),
				"first\r\n-"));

		if (!existingSystemProjections.Contains(ProjectionNamesBuilder.StandardProjections
			.EventByCategoryStandardProjection))
			systemProjections.Add(CreateSystemProjectionPost(
				ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection,
				typeof(CategorizeEventsByStreamPath),
				"first\r\n-"));

		if (!existingSystemProjections.Contains(ProjectionNamesBuilder.StandardProjections
			.EventByTypeStandardProjection))
			systemProjections.Add(CreateSystemProjectionPost(
				ProjectionNamesBuilder.StandardProjections.EventByTypeStandardProjection,
				typeof(IndexEventsByEventType),
				""));

		if (!existingSystemProjections.Contains(ProjectionNamesBuilder.StandardProjections
			.EventByCorrIdStandardProjection))
			systemProjections.Add(CreateSystemProjectionPost(
				ProjectionNamesBuilder.StandardProjections.EventByCorrIdStandardProjection,
				typeof(ByCorrelationId),
				"{\"correlationIdProperty\":\"$correlationId\"}"));

		IEnvelope envelope = new NoopEnvelope();
		var postBatchMessage = new ProjectionManagementMessage.Command.PostBatch(
			envelope, ProjectionManagementMessage.RunAs.System, systemProjections.ToArray());
		_publisher.Publish(postBatchMessage);
	}

	private ProjectionManagementMessage.Command.PostBatch.ProjectionPost CreateSystemProjectionPost
		(string name, Type handlerType, string config) {
		return new ProjectionManagementMessage.Command.PostBatch.ProjectionPost(
			ProjectionMode.Continuous,
			ProjectionManagementMessage.RunAs.System,
			name,
			"native:" + handlerType.Namespace + "." + handlerType.Name,
			config,
			enabled: false,
			checkpointsEnabled: true,
			emitEnabled: true,
			trackEmittedStreams: false,
			enableRunAs: true);
	}

	private void PostNewTransientProjection(PendingProjection projection, IEnvelope replyEnvelope) {
		var initializer = projection.CreateInitializer(replyEnvelope);
		ReadProjectionPossibleStream(projection.Name,
			m => ReadProjectionPossibleStreamCompleted
				(m, initializer, replyEnvelope));
	}

	private void PostNewProjections
		(IDictionary<string, PendingProjection> newProjections, long expectedVersion, IEnvelope replyEnvelope) {
		var corrId = Guid.NewGuid();
		var events = new List<Event>();

		foreach (var projection in newProjections.Values) {
			if (projection.Mode >= ProjectionMode.OneTime) {
				var eventId = Guid.NewGuid();
				events.Add(new Event(
					eventId,
					ProjectionEventTypes.ProjectionCreated,
					false,
					Helper.UTF8NoBom.GetBytes(projection.Name),
					Empty.ByteArray, SchemaInfo.None, SchemaInfo.None));
			} else {
				_logger.Warning("PROJECTIONS: Should not be processing transient projections here.");
			}
		}

		if (!events.Any())
			return;

		var writeEvents = new ClientMessage.WriteEvents(
			corrId,
			corrId,
			_writeDispatcher.Envelope,
			true,
			ProjectionNamesBuilder.ProjectionsRegistrationStream,
			expectedVersion,
			events.ToArray(),
			SystemAccounts.System);

		_isWritePending = true;
		_writeDispatcher.Publish(
			writeEvents,
			m => WriteNewProjectionsCompleted
				(m, writeEvents, newProjections, replyEnvelope));
	}


	private void WriteNewProjectionsCompleted(ClientMessage.WriteEventsCompleted completed,
		ClientMessage.WriteEvents write,
		IDictionary<string, PendingProjection> newProjections,
		IEnvelope envelope, int retryCount = ProjectionCreationRetryCount) {

		_isWritePending = false;
		if (completed.Result == OperationResult.Success) {
			foreach (var name in newProjections.Keys)
				_projectionsRegistrationState.Add(name);

			_projectionsRegistrationExpectedVersion = completed.LastEventNumber;
			StartNewlyRegisteredProjections(newProjections, OnProjectionsRegistrationCaughtUp, envelope);
			return;
		}

		_logger.Information(
			"PROJECTIONS: Created event for projections has not been written to {stream}: {projections}. Error: {error}",
			ProjectionNamesBuilder.ProjectionsRegistrationStream,
			newProjections.Keys,
			Enum.GetName(typeof(OperationResult), completed.Result));

		if (completed.Result == OperationResult.ForwardTimeout ||
			completed.Result == OperationResult.PrepareTimeout ||
			completed.Result == OperationResult.CommitTimeout) {
			if (retryCount > 0) {
				_logger.Information("PROJECTIONS: Retrying write projection creations for {projections}",
					newProjections.Keys);
				_isWritePending = true;
				_writeDispatcher.Publish(
					write,
					m => WriteNewProjectionsCompleted
						(m, write, newProjections, envelope, retryCount - 1));
				return;
			}
		}

		envelope.ReplyWith(new ProjectionManagementMessage.OperationFailed(
			string.Format(
				"The projections '{0}' could not be created because the registration could not be written due to {1}",
				string.Join(", ", newProjections.Keys), completed.Result)));
	}

	private void StartNewlyRegisteredProjections
		(IDictionary<string, PendingProjection> newProjections,
		Action completedAction, IEnvelope replyEnvelope) {

		if (!newProjections.Any()) {
			replyEnvelope.ReplyWith(
				new ProjectionManagementMessage.OperationFailed("Projections were invalid"));
			return;
		}

		_logger.Debug(
			"PROJECTIONS: Found the following new projections in {stream}: {projections}",
			ProjectionNamesBuilder.ProjectionsRegistrationStream,
			newProjections.Keys);

		try {
			foreach (var projection in newProjections.Values) {
				var initializer = projection.CreateInitializer(replyEnvelope);
				ReadProjectionPossibleStream(projection.Name,
					m => ReadProjectionPossibleStreamCompleted
						(m, initializer, replyEnvelope));
			}
		} catch (Exception ex) {
			replyEnvelope.ReplyWith(new ProjectionManagementMessage.OperationFailed(ex.Message));
		}

		completedAction();
	}

	private void ReadProjectionPossibleStream(
		string projectionName,
		Action<ClientMessage.ReadStreamEventsBackwardCompleted> onComplete) {
		var corrId = Guid.NewGuid();
		_readDispatcher.Publish(
			new ClientMessage.ReadStreamEventsBackward(
				corrId,
				corrId,
				_readDispatcher.Envelope,
				ProjectionNamesBuilder.ProjectionsStreamPrefix + projectionName,
				0,
				_readEventsBatchSize,
				resolveLinkTos: false,
				requireLeader: false,
				validationStreamVersion: null,
				user: SystemAccounts.System,
				expires: DateTime.MaxValue),
			new ReadStreamEventsBackwardHandlers.Optimistic(onComplete));
	}

	private void ReadProjectionPossibleStreamCompleted(
		ClientMessage.ReadStreamEventsBackwardCompleted completed,
		NewProjectionInitializer initializer,
		IEnvelope replyEnvelope) {

		long version = -1;
		if (completed.Result == ReadStreamResult.Success) {
			version = completed.LastEventNumber + 1;
		}

		try {
			int queueIndex = GetNextWorkerIndex();
			initializer.
				CreateAndInitializeNewProjection(this, Guid.NewGuid(), _workers[queueIndex],
				version: version);
		} catch (Exception ex) {
			replyEnvelope.ReplyWith(new ProjectionManagementMessage.OperationFailed(ex.Message));
		}
	}

	private void OnProjectionsRegistrationCaughtUp() {
		_logger.Debug($"PROJECTIONS: Caught up with projections registration. Next expected version: {_projectionsRegistrationExpectedVersion}");
	}

	private void WriteProjectionDeletedCompleted(ClientMessage.WriteEventsCompleted writeCompleted,
		ClientMessage.WriteEvents writeDelete,
		ProjectionManagementMessage.Internal.Deleted message,
		Action<long> onCompleted,
		int retryCount) {

		_isWritePending = false;
		if (writeCompleted.Result == OperationResult.Success) {
			onCompleted?.Invoke(writeCompleted.LastEventNumber);
			return;
		}

		_logger.Information(
			"PROJECTIONS: Projection '{projection}' deletion has not been written to {stream}. Error: {e}",
			message.Name,
			ProjectionNamesBuilder.ProjectionsRegistrationStream,
			Enum.GetName(typeof(OperationResult), writeCompleted.Result));

		if (writeCompleted.Result == OperationResult.CommitTimeout || writeCompleted.Result == OperationResult.ForwardTimeout
																   || writeCompleted.Result == OperationResult.PrepareTimeout) {
			if (retryCount > 0) {
				_logger.Information("PROJECTIONS: Retrying write projection deletion for {projection}", message.Name);
				_isWritePending = true;
				_writeDispatcher.Publish(
					writeDelete,
					m => WriteProjectionDeletedCompleted(m, writeDelete, message, onCompleted, retryCount - 1));
				return;
			}
		}

		if (writeCompleted.Result == OperationResult.WrongExpectedVersion) {
			_logger.Error("PROJECTIONS: Got wrong expected version writing projection deletion for {projection}.",
				message.Name);
			return;
		}

		_logger.Error(
			"PROJECTIONS: The projection '{0}' could not be deleted because the deletion event could not be written due to {1}",
			message.Name, writeCompleted.Result);
	}

	private void DelayMessage(Message messageToDelay) {
		_publisher.Publish(TimerMessage.Schedule.Create(TimeSpan.FromSeconds(2), _inputQueue, messageToDelay));
	}

	public class NewProjectionInitializer {
		private readonly long _projectionId;
		private readonly bool _enabled;
		private readonly string _handlerType;
		private readonly string _query;
		private readonly ProjectionMode _projectionMode;
		private readonly bool _emitEnabled;
		private readonly bool _checkpointsEnabled;
		private readonly bool _trackEmittedStreams;
		private readonly bool _enableRunAs;
		private readonly ProjectionManagementMessage.RunAs _runAs;
		private readonly IEnvelope _replyEnvelope;
		private readonly string _name;

		public NewProjectionInitializer(
			long projectionId,
			string name,
			ProjectionMode projectionMode,
			string handlerType,
			string query,
			bool enabled,
			bool emitEnabled,
			bool checkpointsEnabled,
			bool enableRunAs,
			bool trackEmittedStreams,
			ProjectionManagementMessage.RunAs runAs,
			IEnvelope replyEnvelope) {
			if (projectionMode >= ProjectionMode.Continuous && !checkpointsEnabled)
				throw new InvalidOperationException("Continuous mode requires checkpoints");

			if (emitEnabled && !checkpointsEnabled)
				throw new InvalidOperationException("Emit requires checkpoints");

			_projectionId = projectionId;
			_enabled = enabled;
			_handlerType = handlerType;
			_query = query;
			_projectionMode = projectionMode;
			_emitEnabled = emitEnabled;
			_checkpointsEnabled = checkpointsEnabled;
			_trackEmittedStreams = trackEmittedStreams;
			_enableRunAs = enableRunAs;
			_runAs = runAs;
			_replyEnvelope = replyEnvelope;
			_name = name;
		}

		public void CreateAndInitializeNewProjection(
			ProjectionManager projectionManager,
			Guid projectionCorrelationId,
			Guid workerId,
			long? version = -1) {
			var projection = projectionManager.CreateManagedProjectionInstance(
				_name,
				_projectionId,
				projectionCorrelationId,
				workerId);
			projection.InitializeNew(
				new ManagedProjection.PersistedState {
					Enabled = _enabled,
					HandlerType = _handlerType,
					Query = _query,
					Mode = _projectionMode,
					EmitEnabled = _emitEnabled,
					CheckpointsDisabled = !_checkpointsEnabled,
					TrackEmittedStreams = _trackEmittedStreams,
					CheckpointHandledThreshold = ProjectionConsts.CheckpointHandledThreshold,
					CheckpointAfterMs = (int)ProjectionConsts.CheckpointAfterMs.TotalMilliseconds,
					MaxAllowedWritesInFlight = ProjectionConsts.MaxAllowedWritesInFlight,
					Epoch = -1,
					Version = version,
					RunAs = _enableRunAs ? SerializedRunAs.SerializePrincipal(_runAs) : null,
					ProjectionSubsystemVersion = ProjectionsSubsystem.VERSION,
					ProjectionExecutionTimeout = null
				},
				_replyEnvelope);
		}
	}

	private ManagedProjection CreateManagedProjectionInstance(
		string name,
		long projectionId,
		Guid projectionCorrelationId,
		Guid workerID) {
		var enabledToRun = IsProjectionEnabledToRunByMode(name);
		var workerId = workerID;
		var managedProjectionInstance = new ManagedProjection(
			workerId,
			projectionCorrelationId,
			projectionId,
			name,
			enabledToRun,
			_logger,
			_streamDispatcher,
			_writeDispatcher,
			_readDispatcher,
			_publisher,
			_timeProvider,
			_getStateDispatcher,
			_getResultDispatcher,
			_ioDispatcher,
			_projectionsQueryExpiry);

		_projectionsMap.Add(projectionCorrelationId, name);
		_projections.Add(name, managedProjectionInstance);
		_logger.Debug("Adding projection {projectionCorrelationId}@{projection} to list", projectionCorrelationId,
			name);
		return managedProjectionInstance;
	}

	private int GetNextWorkerIndex() {
		if (_lastUsedQueue >= _workers.Length)
			_lastUsedQueue = 0;
		var queueIndex = _lastUsedQueue;
		_lastUsedQueue++;
		return queueIndex;
	}

	public class PendingProjection {
		public ProjectionMode Mode { get; }
		public SerializedRunAs RunAs { get; }
		public string Name { get; }
		public string HandlerType { get; }
		public string Query { get; }
		public bool Enabled { get; }
		public bool CheckpointsEnabled { get; }
		public bool EmitEnabled { get; }
		public bool EnableRunAs { get; }
		public bool TrackEmittedStreams { get; }
		public long ProjectionId { get; }

		public PendingProjection(
			long projectionId, ProjectionMode mode, SerializedRunAs runAs, string name, string handlerType, string query,
			bool enabled, bool checkpointsEnabled, bool emitEnabled, bool enableRunAs,
			bool trackEmittedStreams) {
			ProjectionId = projectionId;
			Mode = mode;
			RunAs = runAs;
			Name = name;
			HandlerType = handlerType;
			Query = query;
			Enabled = enabled;
			CheckpointsEnabled = checkpointsEnabled;
			EmitEnabled = emitEnabled;
			EnableRunAs = enableRunAs;
			TrackEmittedStreams = trackEmittedStreams;
		}

		public PendingProjection(long projectionId, ProjectionManagementMessage.Command.PostBatch.ProjectionPost projection)
			: this(projectionId, projection.Mode, projection.RunAs, projection.Name, projection.HandlerType,
				projection.Query, projection.Enabled, projection.CheckpointsEnabled,
				projection.EmitEnabled, projection.EnableRunAs, projection.TrackEmittedStreams) { }

		public PendingProjection(long projectionId, ProjectionManagementMessage.Command.Post projection)
			: this(projectionId, projection.Mode, projection.RunAs, projection.Name, projection.HandlerType,
				projection.Query, projection.Enabled, projection.CheckpointsEnabled,
				projection.EmitEnabled, projection.EnableRunAs, projection.TrackEmittedStreams) { }

		public NewProjectionInitializer CreateInitializer(IEnvelope replyEnvelope) {
			return new NewProjectionInitializer(
				ProjectionId,
				Name,
				Mode,
				HandlerType,
				Query,
				Enabled,
				EmitEnabled,
				CheckpointsEnabled,
				EnableRunAs,
				TrackEmittedStreams,
				RunAs,
				replyEnvelope);
		}
	}

	public void Handle(TelemetryMessage.Request message) {
		var customProjectionCount = 0;
		var customProjectionRunningCount = 0;
		var standardProjectionCount = 0;
		var standardProjectionRunningCount = 0;

		foreach (var proj in _projections.Values) {
			if (proj.Mode != ProjectionMode.Continuous)
				continue;

			var stats = proj.GetStatistics();

			if (stats.Name.StartsWith("$")) {
				standardProjectionCount += 1;

				if (stats.Status.Contains("Running"))
					standardProjectionRunningCount += 1;
			} else {
				customProjectionCount += 1;

				if (stats.Status.Contains("Running"))
					customProjectionRunningCount += 1;
			}
		}

		message.Envelope.ReplyWith(new TelemetryMessage.Response("projections", new JsonObject {
			["customProjectionCount"] = customProjectionCount,
			["standardProjectionCount"] = standardProjectionCount,
			["customProjectionRunningCount"] = customProjectionRunningCount,
			["standardProjectionRunningCount"] = standardProjectionRunningCount,
			["totalCount"] = customProjectionCount + standardProjectionCount,
			["totalRunningCount"] = customProjectionRunningCount + standardProjectionRunningCount,
		}));
	}
}
