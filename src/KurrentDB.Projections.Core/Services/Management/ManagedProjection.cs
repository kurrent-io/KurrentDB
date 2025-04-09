// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using FastSerialization;
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
using KurrentDB.Projections.Core.Services.Management.ManagedProjectionStates;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Utils;
using Serilog;
using ILogger = Serilog.ILogger;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;

namespace KurrentDB.Projections.Core.Services.Management;


public static class PersistedStateExtensions {
	public static Boolean CheckpointStreamNeedsDeleted(this ManagedProjection.PersistedState persistedState) {
		return persistedState.DeleteCheckpointStream && persistedState.CheckpointsDisabled.GetValueOrDefault() == false;
	}

	public static Boolean EmitStreamNeedsDeleted(this ManagedProjection.PersistedState persistedState) {
		return (persistedState.DeleteEmittedStreams && persistedState.EmitEnabled.GetValueOrDefault());
	}
}

/// <summary>
/// managed projection controls start/stop/create/update/delete lifecycle of the projection.
/// </summary>
public class ManagedProjection : IDisposable {
	public class PersistedState {
		public string HandlerType { get; set; }
		public string Query { get; set; }
		public ProjectionMode Mode { get; set; }
		public bool Enabled { get; set; }
		public bool Deleted { get; set; }
		public bool Deleting { get; set; }
		public bool DeleteEmittedStreams { get; set; }
		public bool DeleteCheckpointStream { get; set; }
		public bool DeleteStateStream { get; set; }
		public int NumberOfPrequisitesMetForDeletion;
		public string Message { get; set; }

		public ProjectionSourceDefinition SourceDefinition { get; set; }
		public bool? EmitEnabled { get; set; }
		public bool? CreateTempStreams { get; set; }
		public bool? CheckpointsDisabled { get; set; }
		public bool? TrackEmittedStreams { get; set; }
		public long? Epoch { get; set; }
		public long? Version { get; set; }
		public SerializedRunAs RunAs { get; set; }
		public int CheckpointHandledThreshold { get; set; }
		public int CheckpointAfterMs { get; set; }
		public int CheckpointUnhandledBytesThreshold { get; set; }
		public int PendingEventsThreshold { get; set; }
		public int MaxWriteBatchLength { get; set; }
		public int MaxAllowedWritesInFlight { get; set; }
		public int? ProjectionSubsystemVersion { get; set; }

		public int? ProjectionExecutionTimeout { get; set; }

		public PersistedState() {
			CheckpointHandledThreshold = ProjectionConsts.CheckpointHandledThreshold;
			CheckpointAfterMs = (int)ProjectionConsts.CheckpointAfterMs.TotalMilliseconds;
			CheckpointUnhandledBytesThreshold = ProjectionConsts.CheckpointUnhandledBytesThreshold;
			PendingEventsThreshold = ProjectionConsts.PendingEventsThreshold;
			MaxWriteBatchLength = ProjectionConsts.MaxWriteBatchLength;
			MaxAllowedWritesInFlight = ProjectionConsts.MaxAllowedWritesInFlight;
		}
	}

	private readonly IPublisher _output;

	private readonly RequestResponseDispatcher<ClientMessage.DeleteStream, ClientMessage.DeleteStreamCompleted>
		_streamDispatcher;

	private readonly RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted>
		_writeDispatcher;

	private readonly ReadDispatcher _readDispatcher;

	private readonly
		RequestResponseDispatcher
		<CoreProjectionManagementMessage.GetState, CoreProjectionStatusMessage.StateReport>
		_getStateDispatcher;

	private readonly
		RequestResponseDispatcher
		<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>
		_getResultDispatcher;


	private readonly ILogger _logger;
	private readonly ITimeProvider _timeProvider;
	private readonly Guid _workerId;
	private readonly Guid _id;
	private readonly long _projectionId;
	private readonly string _name;
	private readonly bool _enabledToRun;
	private ManagedProjectionState _state;
	internal PersistedState PersistedProjectionState = new PersistedState();

	private bool _persistedStateLoaded = false;

	private string _faultedReason;

	private ProjectionStatistics _lastReceivedStatistics;
	private DateTime _lastAccessed;
	private long _lastWrittenVersion = -1;

	private ClaimsPrincipal _runAs;
	internal bool Prepared;
	internal bool Created;
	private bool _pendingWritePersistedState;
	private readonly TimeSpan _projectionsQueryExpiry;

	private ManagedProjectionStateBase _stateHandler;
	private IEnvelope _lastReplyEnvelope;
	private bool _writing;
	private IODispatcher _ioDispatcher;
	private ProjectionConfig _projectionConfig;
	private IEmittedStreamsDeleter _emittedStreamsDeleter;

	public ManagedProjection(
		Guid workerId,
		Guid id,
		long projectionId,
		string name,
		bool enabledToRun,
		ILogger logger,
		RequestResponseDispatcher<ClientMessage.DeleteStream, ClientMessage.DeleteStreamCompleted> streamDispatcher,
		RequestResponseDispatcher<ClientMessage.WriteEvents, ClientMessage.WriteEventsCompleted> writeDispatcher,
		ReadDispatcher readDispatcher,
		IPublisher output,
		ITimeProvider timeProvider,
		RequestResponseDispatcher<CoreProjectionManagementMessage.GetState, CoreProjectionStatusMessage.StateReport>
			getStateDispatcher,
		RequestResponseDispatcher
			<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>
			getResultDispatcher,
		IODispatcher ioDispatcher,
		TimeSpan projectionQueryExpiry) {
		if (id == Guid.Empty)
			throw new ArgumentException("id");
		if (name == null)
			throw new ArgumentNullException("name");
		if (output == null)
			throw new ArgumentNullException("output");
		if (getStateDispatcher == null)
			throw new ArgumentNullException("getStateDispatcher");
		if (getResultDispatcher == null)
			throw new ArgumentNullException("getResultDispatcher");
		if (name == "")
			throw new ArgumentException("name");
		_workerId = workerId;
		_id = id;
		_projectionId = projectionId;
		_name = name;
		_enabledToRun = enabledToRun;
		_logger = logger ?? Log.ForContext<ManagedProjection>();
		_streamDispatcher = streamDispatcher;
		_writeDispatcher = writeDispatcher;
		_readDispatcher = readDispatcher;
		_output = output;
		_timeProvider = timeProvider;
		_getStateDispatcher = getStateDispatcher;
		_getResultDispatcher = getResultDispatcher;
		_lastAccessed = _timeProvider.UtcNow;
		_ioDispatcher = ioDispatcher;
		_projectionsQueryExpiry = projectionQueryExpiry;
	}

	private string HandlerType {
		get { return PersistedProjectionState.HandlerType; }
	}

	private string Query {
		get { return PersistedProjectionState.Query; }
	}

	private bool Enabled {
		get { return PersistedProjectionState.Enabled; }
		set { PersistedProjectionState.Enabled = value; }
	}

	private bool IsMultiStream {
		get {
			return PersistedProjectionState.SourceDefinition != null &&
				   PersistedProjectionState.SourceDefinition.Streams != null &&
				   PersistedProjectionState.SourceDefinition.Streams.Length > 1;
		}
	}

	public bool Deleted {
		get { return PersistedProjectionState.Deleted; }
		private set { PersistedProjectionState.Deleted = value; }
	}

	public bool Deleting {
		get { return PersistedProjectionState.Deleting; }
		private set { PersistedProjectionState.Deleting = value; }
	}

	public Guid Id {
		get { return _id; }
	}

	public ProjectionMode Mode {
		get { return PersistedProjectionState.Mode; }
	}

	public ClaimsPrincipal RunAs {
		get { return _runAs; }
	}

	public bool EnableContentTypeValidation {
		get {
			return PersistedProjectionState.ProjectionSubsystemVersion >=
				   ProjectionsSubsystem.CONTENT_TYPE_VALIDATION_VERSION;
		}
	}

	internal void SetState(ManagedProjectionState value) {
		_state = value;
		switch (value) {
			case ManagedProjectionState.Aborted:
				_stateHandler = new AbortedState(this);
				break;
			case ManagedProjectionState.Aborting:
				_stateHandler = new AbortingState(this);
				break;
			case ManagedProjectionState.Completed:
				_stateHandler = new CompletedState(this);
				break;
			case ManagedProjectionState.Creating:
			case ManagedProjectionState.Loading:
			case ManagedProjectionState.Loaded:
				_stateHandler = new CreatingLoadingLoadedState(this);
				break;
			case ManagedProjectionState.Faulted:
				_stateHandler = new FaultedState(this);
				break;
			case ManagedProjectionState.LoadingStopped:
				_stateHandler = new LoadingStateState(this);
				break;
			case ManagedProjectionState.Prepared:
				_stateHandler = new PreparedState(this);
				break;
			case ManagedProjectionState.Preparing:
				_stateHandler = new PreparingState(this);
				break;
			case ManagedProjectionState.Running:
				_stateHandler = new RunningState(this);
				break;
			case ManagedProjectionState.Starting:
				_stateHandler = new StartingState(this);
				break;
			case ManagedProjectionState.Stopped:
				_stateHandler = new StoppedState(this);
				break;
			case ManagedProjectionState.Stopping:
				_stateHandler = new StoppingState(this);
				break;
			case ManagedProjectionState.Deleting:
				_stateHandler = new DeletingState(this);
				break;
			default:
				throw InvalidProjectionState(value);
		}
	}

	public void Dispose() {
		DisposeCoreProjection();
	}

	public ProjectionStatistics GetStatistics() {
		ProjectionStatistics status;
		if (_lastReceivedStatistics == null) {
			status = new ProjectionStatistics {
				Name = _name,
				EffectiveName = _name,
				ProjectionId = _projectionId,
				Epoch = -1,
				Version = -1,
				Mode = Mode,
				Status = _state.EnumValueName(),
				LeaderStatus = _state
			};
		} else {
			status = _lastReceivedStatistics.Clone();
			status.Mode = Mode;
			status.Name = _name;
			status.ProjectionId = _projectionId;
			var enabledSuffix =
				((_state == ManagedProjectionState.Stopped || _state == ManagedProjectionState.Faulted) && Enabled
					? " (Enabled)"
					: "");
			status.Status = (status.Status == "Stopped" && _state == ManagedProjectionState.Completed
								? _state.EnumValueName()
								: (!status.Status.StartsWith(_state.EnumValueName())
									? _state.EnumValueName() + "/" + status.Status
									: status.Status)) + enabledSuffix;
			status.LeaderStatus = _state;
		}

		if (_state == ManagedProjectionState.Faulted)
			status.StateReason = _faultedReason;
		status.Enabled = Enabled;
		return status;
	}

	public void Handle(ProjectionManagementMessage.Command.GetState message) {
		_lastAccessed = _timeProvider.UtcNow;
		if (_state >= ManagedProjectionState.Running) {
			_getStateDispatcher.Publish(
				new CoreProjectionManagementMessage.GetState(Guid.NewGuid(), Id, message.Partition, _workerId),
				m =>
					message.Envelope.ReplyWith(
						new ProjectionManagementMessage.ProjectionState(_name, m.Partition, m.State, m.Position)));
		} else {
			message.Envelope.ReplyWith(
				new ProjectionManagementMessage.ProjectionState(
					message.Name, message.Partition, "", position: null));
		}
	}

	public void Handle(ProjectionManagementMessage.Command.GetResult message) {
		_lastAccessed = _timeProvider.UtcNow;
		if (_state >= ManagedProjectionState.Running) {
			_getResultDispatcher.Publish(
				new CoreProjectionManagementMessage.GetResult(Guid.NewGuid(), Id, message.Partition, _workerId),
				m =>
					message.Envelope.ReplyWith(
						new ProjectionManagementMessage.ProjectionResult(_name, m.Partition, m.Result,
							m.Position)));
		} else {
			message.Envelope.ReplyWith(
				new ProjectionManagementMessage.ProjectionResult(
					message.Name, message.Partition, "", position: null));
		}
	}

	public void Handle(ProjectionManagementMessage.Command.GetQuery message) {
		_lastAccessed = _timeProvider.UtcNow;

		var emitEnabled = PersistedProjectionState.EmitEnabled ?? false;

		var projectionOutputConfig = new ProjectionOutputConfig {
			ResultStreamName =
				PersistedProjectionState.SourceDefinition == null
					? ""
					: new ProjectionNamesBuilder(_name, PersistedProjectionState.SourceDefinition)
						.GetResultStreamName()
		};

		message.Envelope.ReplyWith(
			new ProjectionManagementMessage.ProjectionQuery(
				_name,
				Query,
				emitEnabled,
				PersistedProjectionState.Mode.ToString(),
				PersistedProjectionState.TrackEmittedStreams,
				!PersistedProjectionState.CheckpointsDisabled,
				PersistedProjectionState.SourceDefinition,
				projectionOutputConfig));
	}

	public void Handle(ProjectionManagementMessage.Command.UpdateQuery message) {
		_lastAccessed = _timeProvider.UtcNow;

		Prepared = false;
		UpdateQuery(message);
		UpdateProjectionVersion();
		SetLastReplyEnvelope(message.Envelope);
		StopUnlessPreparedOrLoaded();
	}

	public void Handle(ProjectionManagementMessage.Command.Disable message) {
		_lastAccessed = _timeProvider.UtcNow;
		SetLastReplyEnvelope(message.Envelope);
		Disable();
		UpdateProjectionVersion();
		StopUnlessPreparedOrLoaded();
	}

	public void Handle(ProjectionManagementMessage.Command.Abort message) {
		_lastAccessed = _timeProvider.UtcNow;
		UpdateProjectionVersion();
		SetLastReplyEnvelope(message.Envelope);
		Disable();
		Abort();
	}

	public void Handle(ProjectionManagementMessage.Command.Enable message) {
		_lastAccessed = _timeProvider.UtcNow;
		if (Enabled
			&& !(_state == ManagedProjectionState.Completed || _state == ManagedProjectionState.Faulted
															|| _state == ManagedProjectionState.Aborted ||
															_state == ManagedProjectionState.Loaded
															|| _state == ManagedProjectionState.Prepared ||
															_state == ManagedProjectionState.Stopped)) {
			//Projection is probably Running
			message.Envelope.ReplyWith(new ProjectionManagementMessage.Updated(message.Name));
			return;
		}

		Enable();
		_pendingWritePersistedState = true;
		UpdateProjectionVersion();
		SetLastReplyEnvelope(message.Envelope);
		StopUnlessPreparedOrLoaded();
	}

	public void Handle(ProjectionManagementMessage.Command.SetRunAs message) {
		_lastAccessed = _timeProvider.UtcNow;
		Prepared = false;
		SetRunAs(message);
		UpdateProjectionVersion();
		SetLastReplyEnvelope(message.Envelope);
		StopUnlessPreparedOrLoaded();
	}

	public void Handle(ProjectionManagementMessage.Command.Delete message) {
		if ((_state != ManagedProjectionState.Stopped && _state != ManagedProjectionState.Faulted) &&
			Mode != ProjectionMode.Transient)
			throw new InvalidOperationException("Cannot delete a projection that hasn't been stopped or faulted.");
		_lastAccessed = _timeProvider.UtcNow;

		PersistedProjectionState.DeleteCheckpointStream = message.DeleteCheckpointStream;
		PersistedProjectionState.DeleteStateStream = message.DeleteStateStream;
		PersistedProjectionState.DeleteEmittedStreams = message.DeleteEmittedStreams;

		if (PersistedProjectionState.CheckpointStreamNeedsDeleted()) {
			PersistedProjectionState.NumberOfPrequisitesMetForDeletion++;
		}

		if ((PersistedProjectionState.EmitEnabled ?? false) &&
			((PersistedProjectionState.TrackEmittedStreams ?? false) &&
			 PersistedProjectionState.DeleteEmittedStreams)) {
			PersistedProjectionState.NumberOfPrequisitesMetForDeletion++;
		}

		if ((PersistedProjectionState.EmitEnabled ?? false) && IsMultiStream) {
			PersistedProjectionState.NumberOfPrequisitesMetForDeletion++;
		}

		Delete();
		UpdateProjectionVersion();
		SetLastReplyEnvelope(message.Envelope);
		SetState(ManagedProjectionState.Deleting);
		StopUnlessPreparedOrLoaded();
	}

	public void Handle(ProjectionManagementMessage.Command.GetConfig message) {
		_lastAccessed = _timeProvider.UtcNow;

		var emitEnabled = PersistedProjectionState.EmitEnabled ?? false;
		var trackEmittedStreams = PersistedProjectionState.TrackEmittedStreams ?? false;

		message.Envelope.ReplyWith(
			new ProjectionManagementMessage.ProjectionConfig(emitEnabled, trackEmittedStreams,
				PersistedProjectionState.CheckpointAfterMs,
				PersistedProjectionState.CheckpointHandledThreshold,
				PersistedProjectionState.CheckpointUnhandledBytesThreshold,
				PersistedProjectionState.PendingEventsThreshold,
				PersistedProjectionState.MaxWriteBatchLength,
				PersistedProjectionState.MaxAllowedWritesInFlight,
				PersistedProjectionState.ProjectionExecutionTimeout));
	}

	public void Handle(ProjectionManagementMessage.Command.UpdateConfig message) {
		if ((_state != ManagedProjectionState.Stopped && _state != ManagedProjectionState.Faulted) &&
			Mode != ProjectionMode.Transient)
			throw new InvalidOperationException(
				"Cannot update the config of a projection that hasn't been stopped or faulted.");
		_lastAccessed = _timeProvider.UtcNow;

		PersistedProjectionState.EmitEnabled = message.EmitEnabled;
		PersistedProjectionState.TrackEmittedStreams = message.TrackEmittedStreams;
		PersistedProjectionState.CheckpointAfterMs = message.CheckpointAfterMs;
		PersistedProjectionState.CheckpointHandledThreshold = message.CheckpointHandledThreshold;
		PersistedProjectionState.CheckpointUnhandledBytesThreshold = message.CheckpointUnhandledBytesThreshold;
		PersistedProjectionState.PendingEventsThreshold = message.PendingEventsThreshold;
		PersistedProjectionState.MaxWriteBatchLength = message.MaxWriteBatchLength;
		PersistedProjectionState.MaxAllowedWritesInFlight = message.MaxAllowedWritesInFlight;
		PersistedProjectionState.ProjectionExecutionTimeout = message.ProjectionExecutionTimeout;

		UpdateProjectionVersion();
		_pendingWritePersistedState = true;
		WritePersistedState(CreatePersistedStateEvent(Guid.NewGuid(), PersistedProjectionState,
			ProjectionNamesBuilder.ProjectionsStreamPrefix + _name));

		message.Envelope.ReplyWith(new ProjectionManagementMessage.Updated(message.Name));
	}

	public void DeleteProjectionStreams() {
		var sourceDefinition = PersistedProjectionState.SourceDefinition ?? new ProjectionSourceDefinition();
		var projectionNamesBuilder = new ProjectionNamesBuilder(_name, sourceDefinition);

		if (PersistedProjectionState.EmitStreamNeedsDeleted() && IsMultiStream) {
			DeleteStream(projectionNamesBuilder.GetOrderStreamName(), DeleteIfConditionsAreMet);
		}

		if (PersistedProjectionState.CheckpointStreamNeedsDeleted()) {
			DeleteStream(projectionNamesBuilder.MakeCheckpointStreamName(), DeleteIfConditionsAreMet);
		}

		if (PersistedProjectionState.EmitStreamNeedsDeleted()) {
			if (_emittedStreamsDeleter == null) {
				_emittedStreamsDeleter = new EmittedStreamsDeleter(_ioDispatcher, projectionNamesBuilder.GetEmittedStreamsName(), projectionNamesBuilder.GetEmittedStreamsCheckpointName());
			}

			_emittedStreamsDeleter.DeleteEmittedStreams(DeleteIfConditionsAreMet);
		}

		if (!PersistedProjectionState.CheckpointStreamNeedsDeleted() &&
			!PersistedProjectionState.EmitStreamNeedsDeleted()) {
			DeleteIfConditionsAreMet();
		}
	}

	private void DeleteIfConditionsAreMet() {
		if (Interlocked.Decrement(ref PersistedProjectionState.NumberOfPrequisitesMetForDeletion) <= 0) {
			Deleted = true;
			Deleting = false;
			Reply();
		}
	}

	public void Handle(ProjectionManagementMessage.Command.Reset message) {
		_lastAccessed = _timeProvider.UtcNow;
		Prepared = false;
		_pendingWritePersistedState = true;
		Reset();
		UpdateProjectionVersion();
		SetLastReplyEnvelope(message.Envelope);
		StopUnlessPreparedOrLoaded();
	}

	public void Handle(ProjectionManagementMessage.Internal.CleanupExpired message) {
		if (IsExpiredProjection()) {
			if (_state == ManagedProjectionState.Creating) {
				// NOTE: workaround for stop not working on creating state (just ignore them)
				return;
			}

			_logger.Warning(
				"Transient projection {projection} has expired and will be deleted. Last accessed at {lastAccessed}",
				_name, _lastAccessed);
			Handle(
				new ProjectionManagementMessage.Command.Delete(
					new NoopEnvelope(),
					_name,
					ProjectionManagementMessage.RunAs.System,
					false,
					false,
					false));
		}
	}

	public void Handle(CoreProjectionStatusMessage.Started message) {
		_stateHandler.Started();
	}

	public void Handle(CoreProjectionStatusMessage.Stopped message) {
		_stateHandler.Stopped(message);
		if (Deleting) {
			SetState(ManagedProjectionState.Deleting);
		}
	}

	public void Handle(CoreProjectionStatusMessage.Faulted message) {
		_stateHandler.Faulted(message);
	}

	public void Handle(CoreProjectionStatusMessage.Prepared message) {
		_stateHandler.Prepared(message);
	}

	public void Handle(CoreProjectionStatusMessage.StatisticsReport message) {
		_lastReceivedStatistics = message.Statistics;
	}

	private void SetRunAs(ProjectionManagementMessage.Command.SetRunAs message) {
		PersistedProjectionState.RunAs =
			message.Action == ProjectionManagementMessage.Command.SetRunAs.SetRemove.Set
				? SerializedRunAs.SerializePrincipal(message.RunAs)
				: null;
		_runAs = SerializedRunAs.DeserializePrincipal(PersistedProjectionState.RunAs);
		_pendingWritePersistedState = true;
	}

	private void SetLastReplyEnvelope(IEnvelope envelope) {
		if (_lastReplyEnvelope != null)
			_lastReplyEnvelope.ReplyWith(
				new ProjectionManagementMessage.OperationFailed("Aborted by subsequent operation"));
		_lastReplyEnvelope = envelope;
	}

	private void Reset() {
		UpdateProjectionVersion(force: true);
		PersistedProjectionState.Epoch = PersistedProjectionState.Version;
	}

	private bool IsExpiredProjection() {
		return Mode == ProjectionMode.Transient &&
			   _lastAccessed.Add(_projectionsQueryExpiry) < _timeProvider.UtcNow && _persistedStateLoaded;
	}

	public void InitializeNew(PersistedState persistedState, IEnvelope replyEnvelope) {
		LoadPersistedState(persistedState);
		UpdateProjectionVersion();
		_pendingWritePersistedState = true;
		SetLastReplyEnvelope(replyEnvelope);
		PrepareOrWriteStartOrLoadStopped();
	}

	public void InitializeExisting(string name) {
		SetState(ManagedProjectionState.Loading);
		ReadPersistedState(name);
	}

	private void ReadPersistedState(string name) {
		var corrId = Guid.NewGuid();
		_readDispatcher.Publish(
			new ClientMessage.ReadStreamEventsBackward(
				corrId, corrId, _readDispatcher.Envelope, ProjectionNamesBuilder.ProjectionsStreamPrefix + name, -1,
				1,
				resolveLinkTos: false, requireLeader: false, validationStreamVersion: null,
				user: SystemAccounts.System,
				expires: DateTime.MaxValue),
			new ReadStreamEventsBackwardHandlers.Optimistic(PersistedStateReadCompleted));
	}

	private void PersistedStateReadCompleted(ClientMessage.ReadStreamEventsBackwardCompleted completed) {
		if (completed.Result == ReadStreamResult.Success && completed.Events.Count is 1) {
			var persistedState = completed.Events[0].Event.Data.ParseJson<PersistedState>();

			_lastWrittenVersion = completed.Events[0].Event.EventNumber;
			FixUpOldFormat(completed, persistedState);
			FixupOldProjectionModes(persistedState);
			FixUpOldProjectionRunAs(persistedState);

			LoadPersistedState(persistedState);
			//TODO: encapsulate this into managed projection
			SetState(ManagedProjectionState.Loaded);
			_pendingWritePersistedState = false;
			SetLastReplyEnvelope(null);
			PrepareOrWriteStartOrLoadStopped();
			return;
		}

		SetState(ManagedProjectionState.Creating);

		_logger.Verbose(
			"Projection manager did not find any projection configuration records in the {stream} stream.  Projection stays in CREATING state",
			completed.EventStreamId);
	}

	private void FixUpOldProjectionRunAs(PersistedState persistedState) {
		if (persistedState.RunAs == null || string.IsNullOrEmpty(persistedState.RunAs.Name)) {
			_runAs = SystemAccounts.System;
			persistedState.RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.System);
		}
	}

	private void FixUpOldFormat(ClientMessage.ReadStreamEventsBackwardCompleted completed,
		PersistedState persistedState) {
		if (persistedState.Version == null) {
			persistedState.Version = completed.Events[0].Event.EventNumber;
			persistedState.Epoch = -1;
		}

		if (_lastWrittenVersion > persistedState.Version)
			persistedState.Version = _lastWrittenVersion;
	}

	private void FixupOldProjectionModes(PersistedState persistedState) {
		switch ((int)persistedState.Mode) {
			case 2: // old continuous
				persistedState.Mode = ProjectionMode.Continuous;
				break;
			case 3: // old persistent
				persistedState.Mode = ProjectionMode.Continuous;
				persistedState.EmitEnabled = persistedState.EmitEnabled ?? true;
				break;
		}
	}

	private void LoadPersistedState(PersistedState persistedState) {
		var handlerType = persistedState.HandlerType;
		var query = persistedState.Query;

		if (handlerType == null)
			throw new ArgumentNullException("persistedState", "HandlerType");
		if (query == null)
			throw new ArgumentNullException("persistedState", "Query");
		if (handlerType == "")
			throw new ArgumentException("HandlerType", "persistedState");

		if (_state != ManagedProjectionState.Creating && _state != ManagedProjectionState.Loading)
			throw new InvalidOperationException("LoadPersistedState is now allowed in this state");

		PersistedProjectionState = persistedState;
		_runAs = SerializedRunAs.DeserializePrincipal(persistedState.RunAs);
		_persistedStateLoaded = true;
	}

	internal void WriteStartOrLoadStopped() {
		if (_pendingWritePersistedState)
			WritePersistedState(CreatePersistedStateEvent(Guid.NewGuid(), PersistedProjectionState,
				ProjectionNamesBuilder.ProjectionsStreamPrefix + _name));
		else
			StartOrLoadStopped();
	}

	internal void StartCompleted() {
		Reply();
	}

	private ClientMessage.WriteEvents CreatePersistedStateEvent(Guid correlationId, PersistedState persistedState,
		string eventStreamId) {
		return new ClientMessage.WriteEvents(
			correlationId, correlationId, _writeDispatcher.Envelope, true, eventStreamId, ExpectedVersion.Any,
			new Event(Guid.NewGuid(), ProjectionEventTypes.ProjectionUpdated, true, persistedState.ToJsonBytes(),
				Empty.ByteArray, SchemaInfo.None, SchemaInfo.None),
			SystemAccounts.System);
	}

	private void WritePersistedState(ClientMessage.WriteEvents persistedStateEvent) {
		if (Mode == ProjectionMode.Transient) {
			//TODO: move to common completion procedure
			_lastWrittenVersion = PersistedProjectionState.Version ?? -1;
			_pendingWritePersistedState = false;
			StartOrLoadStopped();
			return;
		}

		_writing = true;
		_writeDispatcher.Publish(
			persistedStateEvent,
			m => WritePersistedStateCompleted(m, persistedStateEvent, persistedStateEvent.EventStreamId));
	}

	private void WritePersistedStateCompleted(ClientMessage.WriteEventsCompleted message,
		ClientMessage.WriteEvents eventToRetry, string eventStreamId) {
		if (!_writing) {
			_logger.Error("Projection definition write completed in non writing state. ({projection})", _name);
		}

		if (message.Result == OperationResult.Success) {
			_logger.Information("'{projection}' projection source has been written", _name);
			_pendingWritePersistedState = false;
			var writtenEventNumber = message.FirstEventNumber;
			if (writtenEventNumber != (PersistedProjectionState.Version ?? writtenEventNumber))
				throw new Exception("Projection version and event number mismatch");
			_lastWrittenVersion = (PersistedProjectionState.Version ?? writtenEventNumber);
			StartOrLoadStopped();
			return;
		}

		_logger.Information(
			"Projection '{projection}' source has not been written to {stream}. Error: {e}",
			_name,
			eventStreamId,
			Enum.GetName(typeof(OperationResult), message.Result));
		if (message.Result == OperationResult.CommitTimeout || message.Result == OperationResult.ForwardTimeout
															|| message.Result == OperationResult.PrepareTimeout
															|| message.Result ==
															OperationResult.WrongExpectedVersion) {
			_logger.Information("Retrying write projection source for {projection}", _name);
			WritePersistedState(eventToRetry);
		} else
			throw new NotSupportedException("Unsupported error code received");
	}

	private void DeleteStream(string streamId, Action completed) {
		//delete checkpoint stream
		var correlationId = Guid.NewGuid();
		_streamDispatcher.Publish(new ClientMessage.DeleteStream(
			correlationId,
			correlationId,
			_writeDispatcher.Envelope,
			true,
			streamId,
			ExpectedVersion.Any,
			false,
			SystemAccounts.System), m => DeleteStreamCompleted(m, streamId, completed));
	}

	private void DeleteStreamCompleted(ClientMessage.DeleteStreamCompleted message, string streamId,
		Action completed) {
		// currently, WrongExpectedVersion is returned when deleting non-existing streams, even when specifying ExpectedVersion.Any.
		// it is not too intuitive but changing the response would break the contract and compatibility with TCP/gRPC/web clients or require adding a new error code to all clients.
		// note: we don't need to check if CurrentVersion == -1 here to make sure it's a non-existing stream since the deletion is done with ExpectedVersion.Any
		if (message.Result == OperationResult.WrongExpectedVersion) {
			// stream was never created
			_logger.Information("PROJECTIONS: Projection Stream '{stream}' was not deleted since it does not exist", streamId);
			completed();
			return;
		}
		if (message.Result == OperationResult.Success || message.Result == OperationResult.StreamDeleted) {
			_logger.Information("PROJECTIONS: Projection Stream '{stream}' deleted", streamId);
			completed();
			return;
		}

		_logger.Information(
			"PROJECTIONS: Projection stream '{stream}' could not be deleted. Error: {e}",
			streamId,
			Enum.GetName(typeof(OperationResult), message.Result));
		if (message.Result == OperationResult.CommitTimeout ||
			message.Result == OperationResult.ForwardTimeout) {
			DeleteStream(streamId, completed);
		} else
			throw new NotSupportedException("Unsupported error code received");
	}

	private void Prepare(Message message) {
		if (_state >= ManagedProjectionState.Preparing) {
			DisposeCoreProjection();
			SetState(ManagedProjectionState.Loaded);
		}

		//note: set running before start as coreProjection.start() can respond with faulted
		SetState(ManagedProjectionState.Preparing);
		_output.Publish(message);
	}

	private void Start() {
		if (!Enabled)
			throw new InvalidOperationException("Projection is disabled");
		SetState(ManagedProjectionState.Starting);
		_output.Publish(new CoreProjectionManagementMessage.Start(Id, _workerId));
	}

	private void LoadStopped() {
		SetState(ManagedProjectionState.LoadingStopped);
		_output.Publish(new CoreProjectionManagementMessage.LoadStopped(Id, _workerId));
	}

	private void DisposeCoreProjection() {
		Created = false;
		_output.Publish(new CoreProjectionManagementMessage.Dispose(Id, _workerId));
	}

	private void Enable() {
		Enabled = true;
	}

	private Message CreateCreateAndPrepareMessage(ProjectionConfig config) {
		return new CoreProjectionManagementMessage.CreateAndPrepare(
				Id,
				_workerId,
				_name,
				new ProjectionVersion(_projectionId, PersistedProjectionState.Epoch ?? 0,
					PersistedProjectionState.Version ?? 0),
				config,
				HandlerType,
				Query,
				EnableContentTypeValidation);
	}

	private CoreProjectionManagementMessage.CreatePrepared CreatePreparedMessage(ProjectionConfig config) {
		if (PersistedProjectionState.SourceDefinition == null)
			throw new Exception(
				"The projection cannot be loaded as stopped as it was stored in the old format.  Update the projection query text to force prepare");

		var createProjectionMessage = new CoreProjectionManagementMessage.CreatePrepared(
			Id,
			_workerId,
			_name,
			new ProjectionVersion(_projectionId, PersistedProjectionState.Epoch ?? 0,
				PersistedProjectionState.Version ?? 1),
			config,
			QuerySourcesDefinition.From(PersistedProjectionState.SourceDefinition),
			HandlerType,
			Query,
			EnableContentTypeValidation);
		return createProjectionMessage;
	}

	private void StopUnlessPreparedOrLoaded() {
		switch (_state) {
			case ManagedProjectionState.Prepared:
			case ManagedProjectionState.Loaded:
				PrepareOrWriteStartOrLoadStopped();
				break;
			case ManagedProjectionState.Stopped:
			case ManagedProjectionState.Completed:
			case ManagedProjectionState.Aborted:
			case ManagedProjectionState.Faulted:
				SetState(ManagedProjectionState.Stopped);
				PrepareOrWriteStartOrLoadStopped();
				return;
			case ManagedProjectionState.Deleting:
				PrepareOrWriteStartOrLoadStopped();
				break;
			case ManagedProjectionState.Loading:
			case ManagedProjectionState.Creating:
				throw new InvalidOperationException(
					string.Format(
						"Cannot stop a projection in the '{0}' state",
						Enum.GetName(typeof(ManagedProjectionState), _state)));
			case ManagedProjectionState.Stopping:
			case ManagedProjectionState.Aborting:
				return;
			case ManagedProjectionState.Running:
			case ManagedProjectionState.Starting:
				SetState(ManagedProjectionState.Stopping);
				_output.Publish(new CoreProjectionManagementMessage.Stop(Id, _workerId));
				break;
			default:
				throw InvalidProjectionState(_state);
		}
	}

	private void Abort() {
		switch (_state) {
			case ManagedProjectionState.Stopped:
			case ManagedProjectionState.Completed:
			case ManagedProjectionState.Aborted:
			case ManagedProjectionState.Faulted:
			case ManagedProjectionState.Loaded:
				PrepareOrWriteStartOrLoadStopped();
				return;
			case ManagedProjectionState.Loading:
			case ManagedProjectionState.Creating:
				throw new InvalidOperationException(
					string.Format(
						"Cannot stop a projection in the '{0}' state",
						Enum.GetName(typeof(ManagedProjectionState), _state)));
			case ManagedProjectionState.Stopping:
				SetState(ManagedProjectionState.Aborting);
				_output.Publish(new CoreProjectionManagementMessage.Kill(Id, _workerId));
				return;
			case ManagedProjectionState.Aborting:
				return;
			case ManagedProjectionState.Running:
			case ManagedProjectionState.Starting:
				SetState(ManagedProjectionState.Aborting);
				_output.Publish(new CoreProjectionManagementMessage.Kill(Id, _workerId));
				break;
			default:
				throw InvalidProjectionState(_state);
		}
	}


	public void Fault(string reason) {
		_logger.Error("The '{projection}' projection faulted due to '{e}'", _name, reason);
		SetState(ManagedProjectionState.Faulted);
		_faultedReason = reason;
	}

	private ProjectionConfig CreateDefaultProjectionConfiguration() {
		var checkpointsEnabled = PersistedProjectionState.CheckpointsDisabled != true;
		var trackEmittedStreams = PersistedProjectionState.TrackEmittedStreams == true;
		var maximumCheckpointCount = checkpointsEnabled ? PersistedProjectionState.CheckpointHandledThreshold : 0;
		var checkpointAfterMs = checkpointsEnabled ? PersistedProjectionState.CheckpointAfterMs : 0;
		var checkpointUnhandledBytesThreshold =
			checkpointsEnabled ? PersistedProjectionState.CheckpointUnhandledBytesThreshold : 0;
		var pendingEventsThreshold = PersistedProjectionState.PendingEventsThreshold;
		var maxWriteBatchLength = PersistedProjectionState.MaxWriteBatchLength;
		var maximumAllowedWritesInFlight = PersistedProjectionState.MaxAllowedWritesInFlight;
		var emitEventEnabled = PersistedProjectionState.EmitEnabled == true;
		var createTempStreams = PersistedProjectionState.CreateTempStreams == true;
		var stopOnEof = PersistedProjectionState.Mode <= ProjectionMode.OneTime;
		var projectionExecutionTimeout = PersistedProjectionState.ProjectionExecutionTimeout;

		var projectionConfig = new ProjectionConfig(
			_runAs,
			maximumCheckpointCount,
			checkpointUnhandledBytesThreshold,
			pendingEventsThreshold,
			maxWriteBatchLength,
			emitEventEnabled,
			checkpointsEnabled,
			createTempStreams,
			stopOnEof,
			trackEmittedStreams,
			checkpointAfterMs,
			maximumAllowedWritesInFlight,
			projectionExecutionTimeout);
		return projectionConfig;
	}

	private void StartOrLoadStopped() {
		if (_state == ManagedProjectionState.Prepared) {
			if (Enabled && _enabledToRun)
				Start();
			else {
				LoadStopped();
			}
		} else if (_state == ManagedProjectionState.Aborted ||
				   _state == ManagedProjectionState.Completed ||
				   _state == ManagedProjectionState.Faulted ||
				   _state == ManagedProjectionState.Stopped ||
				   _state == ManagedProjectionState.Deleting)
			Reply();
		else
			throw InvalidProjectionState(_state);
	}

	private void UpdateQuery(ProjectionManagementMessage.Command.UpdateQuery message) {
		PersistedProjectionState.Query = message.Query;
		PersistedProjectionState.EmitEnabled = message.EmitEnabled ?? PersistedProjectionState.EmitEnabled;
		_pendingWritePersistedState = true;
		if (_state == ManagedProjectionState.Completed) {
			Reset();
		}
	}

	private void Disable() {
		Enabled = false;
		_pendingWritePersistedState = true;
	}

	public void PrepareOrWriteStartOrLoadStopped() {
		if (_state == ManagedProjectionState.Prepared) {
			WriteStartOrLoadStopped();
			return;
		}

		if (Prepared && Created && !(Enabled && _enabledToRun)) {
			WriteStartOrLoadStopped();
			return;
		}

		_projectionConfig = CreateDefaultProjectionConfiguration();

		var prepareMessage = !(Enabled && _enabledToRun) && !_pendingWritePersistedState
			? CreatePreparedMessage(_projectionConfig)
			: CreateCreateAndPrepareMessage(_projectionConfig);

		Prepare(prepareMessage);
	}

	private void Reply() {
		if (_lastReplyEnvelope != null)
			_lastReplyEnvelope.ReplyWith(new ProjectionManagementMessage.Updated(_name));
		_lastReplyEnvelope = null;
		if (Deleted && !_pendingWritePersistedState) {
			DisposeCoreProjection();
			_output.Publish(new ProjectionManagementMessage.Internal.Deleted(_name, Id));
		}
	}

	private void Delete() {
		Enabled = false;
		Deleted = false;
		Deleting = true;
		_pendingWritePersistedState = true;
	}

	private void UpdateProjectionVersion(bool force = false) {
		if (_lastWrittenVersion == PersistedProjectionState.Version) {
			PersistedProjectionState.Version++;
			PersistedProjectionState.ProjectionSubsystemVersion = ProjectionsSubsystem.VERSION;
		} else if (force)
			throw new ApplicationException(
				"Internal error: projection definition must be saved before forced updating version");
	}

	private static InvalidOperationException InvalidProjectionState(ManagedProjectionState state) =>
		new InvalidOperationException($"Did not expect {nameof(ManagedProjectionState)} in state {state}.");
}

public class SerializedRunAs {
	public string Name { get; set; }
	public string[] Roles { get; set; }

	public static implicit operator SerializedRunAs(ProjectionManagementMessage.RunAs runAs) {
		return runAs == null ? null : SerializePrincipal(runAs);
	}

	public static implicit operator ProjectionManagementMessage.RunAs(SerializedRunAs runAs) {
		return new ProjectionManagementMessage.RunAs(DeserializePrincipal(runAs));
	}

	public static SerializedRunAs SerializePrincipal(ProjectionManagementMessage.RunAs runAs) {
		if (runAs?.Principal == null || runAs.Principal.HasClaim(x => x.Type == ClaimTypes.Anonymous))
			return null; // anonymous
		if (runAs.Principal == SystemAccounts.System)
			return new SerializedRunAs { Name = "$system" };

		var principal = runAs.Principal;
		var roles = principal.Claims
			.Where(x => x.Type == ClaimTypes.Role)
			.Select(c => $"{c.Type}$$${c.Value}").ToArray();
		return new SerializedRunAs { Name = runAs.Principal.Identity.Name, Roles = roles };
	}

	public static ClaimsPrincipal DeserializePrincipal(SerializedRunAs runAs) {
		if (runAs?.Name == null)
			return SystemAccounts.Anonymous;
		if (runAs.Name == "$system") //TODO: make sure nobody else uses it
			return SystemAccounts.System;
		var claims = runAs.Roles.Select(x => x.Split("$$$")).Select(x => {
			return x.Length switch {
				1 => new Claim(ClaimTypes.Role, x[0]),
				2 => new Claim(x[0], x[1]),
				_ => throw new SerializationException("Could not deserialize run as")
			};

		}).ToList();
		if (!claims.Exists(x => x.Type == ClaimTypes.Name)) {
			claims.Add(new Claim(ClaimTypes.Name, runAs.Name));
		}

		return new ClaimsPrincipal(new ClaimsIdentity(claims, "ES-Projections"));
	}
}
