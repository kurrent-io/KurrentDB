// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.UserManagement;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using System.Collections.Generic;
using EventStore.Common.Utils;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Messages;

public static partial class ProjectionManagementMessage {
	public static partial class Command {
		[DerivedMessage]
		public abstract partial class ControlMessage : Message {
			private readonly IEnvelope _envelope;
			public readonly RunAs RunAs;

			protected ControlMessage(IEnvelope envelope, RunAs runAs) {
				_envelope = envelope;
				RunAs = runAs;
			}

			public IEnvelope Envelope {
				get { return _envelope; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class PostBatch : ControlMessage {
			public ProjectionPost[] Projections { get; }

			public PostBatch(
				IEnvelope envelope, RunAs runAs, ProjectionPost[] projections)
				: base(envelope, runAs) {
					Projections = projections;
			}

			public class ProjectionPost
			{
				public ProjectionMode Mode { get; }
				public RunAs RunAs {get;}
				public string Name { get; }
				public string HandlerType { get; }
				public string Query { get; }
				public bool Enabled { get;}
				public bool CheckpointsEnabled{ get;}
				public bool EmitEnabled { get; }
				public bool EnableRunAs { get; }
				public bool TrackEmittedStreams { get; }

				public ProjectionPost(
					ProjectionMode mode, RunAs runAs, string name, string handlerType, string query,
					bool enabled, bool checkpointsEnabled, bool emitEnabled, bool enableRunAs,
					bool trackEmittedStreams)
				{
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
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Post : ControlMessage {
			private readonly ProjectionMode _mode;
			private readonly string _name;
			private readonly string _handlerType;
			private readonly string _query;
			private readonly bool _enabled;
			private readonly bool _checkpointsEnabled;
			private readonly bool _emitEnabled;
			private readonly bool _enableRunAs;
			private readonly bool _trackEmittedStreams;

			public Post(
				IEnvelope envelope, ProjectionMode mode, string name, RunAs runAs, string handlerType, string query,
				bool enabled, bool checkpointsEnabled, bool emitEnabled, bool trackEmittedStreams,
				bool enableRunAs = false)
				: base(envelope, runAs) {
				_name = name;
				_handlerType = handlerType;
				_mode = mode;
				_query = query;
				_enabled = enabled;
				_checkpointsEnabled = checkpointsEnabled;
				_emitEnabled = emitEnabled;
				_trackEmittedStreams = trackEmittedStreams;
				_enableRunAs = enableRunAs;
			}

			public Post(
				IEnvelope envelope, ProjectionMode mode, string name, RunAs runAs, Type handlerType, string query,
				bool enabled, bool checkpointsEnabled, bool emitEnabled, bool trackEmittedStreams,
				bool enableRunAs = false)
				: base(envelope, runAs) {
				_name = name;
				_handlerType = "native:" + handlerType.Namespace + "." + handlerType.Name;
				_mode = mode;
				_query = query;
				_enabled = enabled;
				_checkpointsEnabled = checkpointsEnabled;
				_emitEnabled = emitEnabled;
				_trackEmittedStreams = trackEmittedStreams;
				_enableRunAs = enableRunAs;
			}

			// shortcut for posting ad-hoc JS queries
			public Post(IEnvelope envelope, RunAs runAs, string query, bool enabled)
				: base(envelope, runAs) {
				_name = Guid.NewGuid().ToString("D");
				_handlerType = "JS";
				_mode = ProjectionMode.Transient;
				_query = query;
				_enabled = enabled;
				_checkpointsEnabled = false;
				_emitEnabled = false;
				_trackEmittedStreams = false;
			}

			public ProjectionMode Mode {
				get { return _mode; }
			}

			public string Query {
				get { return _query; }
			}

			public string Name {
				get { return _name; }
			}

			public string HandlerType {
				get { return _handlerType; }
			}

			public bool Enabled {
				get { return _enabled; }
			}

			public bool EmitEnabled {
				get { return _emitEnabled; }
			}

			public bool CheckpointsEnabled {
				get { return _checkpointsEnabled; }
			}

			public bool EnableRunAs {
				get { return _enableRunAs; }
			}

			public bool TrackEmittedStreams {
				get { return _trackEmittedStreams; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Disable : ControlMessage {
			private readonly string _name;

			public Disable(IEnvelope envelope, string name, RunAs runAs)
				: base(envelope, runAs) {
				_name = name;
			}

			public string Name {
				get { return _name; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Enable : ControlMessage {
			private readonly string _name;

			public Enable(IEnvelope envelope, string name, RunAs runAs)
				: base(envelope, runAs) {
				_name = name;
			}

			public string Name {
				get { return _name; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Abort : ControlMessage {
			private readonly string _name;

			public Abort(IEnvelope envelope, string name, RunAs runAs)
				: base(envelope, runAs) {
				_name = name;
			}

			public string Name {
				get { return _name; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class SetRunAs : ControlMessage {
			public enum SetRemove {
				Set,
				Remove
			};

			private readonly string _name;
			private readonly SetRemove _action;

			public SetRunAs(IEnvelope envelope, string name, RunAs runAs, SetRemove action)
				: base(envelope, runAs) {
				_name = name;
				_action = action;
			}

			public string Name {
				get { return _name; }
			}

			public SetRemove Action {
				get { return _action; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class UpdateQuery : ControlMessage {
			private readonly string _name;
			private readonly string _query;
			private readonly bool? _emitEnabled;

			public UpdateQuery(
				IEnvelope envelope, string name, RunAs runAs, string query, bool? emitEnabled)
				: base(envelope, runAs) {
				_name = name;
				_query = query;
				_emitEnabled = emitEnabled;
			}

			public string Query {
				get { return _query; }
			}

			public string Name {
				get { return _name; }
			}

			public bool? EmitEnabled {
				get { return _emitEnabled; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Reset : ControlMessage {
			private readonly string _name;

			public Reset(IEnvelope envelope, string name, RunAs runAs)
				: base(envelope, runAs) {
				_name = name;
			}

			public string Name {
				get { return _name; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Delete : ControlMessage {
			private readonly string _name;
			private readonly bool _deleteCheckpointStream;
			private readonly bool _deleteStateStream;
			private readonly bool _deleteEmittedStreams;

			public Delete(
				IEnvelope envelope, string name, RunAs runAs, bool deleteCheckpointStream, bool deleteStateStream,
				bool deleteEmittedStreams)
				: base(envelope, runAs) {
				_name = name;
				_deleteCheckpointStream = deleteCheckpointStream;
				_deleteStateStream = deleteStateStream;
				_deleteEmittedStreams = deleteEmittedStreams;
			}

			public string Name {
				get { return _name; }
			}

			public bool DeleteCheckpointStream {
				get { return _deleteCheckpointStream; }
			}

			public bool DeleteStateStream {
				get { return _deleteStateStream; }
			}

			public bool DeleteEmittedStreams {
				get { return _deleteEmittedStreams; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetQuery : ControlMessage {
			private readonly string _name;

			public GetQuery(IEnvelope envelope, string name, RunAs runAs) :
				base(envelope, runAs) {
				_name = name;
			}

			public string Name {
				get { return _name; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetConfig : ControlMessage {
			private readonly string _name;

			public GetConfig(IEnvelope envelope, string name, RunAs runAs) :
				base(envelope, runAs) {
				_name = name;
			}

			public string Name {
				get { return _name; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class UpdateConfig : ControlMessage {
			private readonly string _name;
			private readonly bool _emitEnabled;
			private readonly bool _trackEmittedStreams;
			private readonly int _checkpointAfterMs;
			private readonly int _checkpointHandledThreshold;
			private readonly int _checkpointUnhandledBytesThreshold;
			private readonly int _pendingEventsThreshold;
			private readonly int _maxWriteBatchLength;
			private readonly int _maxAllowedWritesInFlight;
			private readonly int? _projectionExecutionTimeout;

			public UpdateConfig(IEnvelope envelope, string name, bool emitEnabled, bool trackEmittedStreams,
				int checkpointAfterMs,
				int checkpointHandledThreshold, int checkpointUnhandledBytesThreshold, int pendingEventsThreshold,
				int maxWriteBatchLength, int maxAllowedWritesInFlight, RunAs runAs, int? projectionExecutionTimeout) :
				base(envelope, runAs) {
				_name = name;
				_emitEnabled = emitEnabled;
				_trackEmittedStreams = trackEmittedStreams;
				_checkpointAfterMs = checkpointAfterMs;
				_checkpointHandledThreshold = checkpointHandledThreshold;
				_checkpointUnhandledBytesThreshold = checkpointUnhandledBytesThreshold;
				_pendingEventsThreshold = pendingEventsThreshold;
				_maxWriteBatchLength = maxWriteBatchLength;
				_maxAllowedWritesInFlight = maxAllowedWritesInFlight;
				_projectionExecutionTimeout = projectionExecutionTimeout;
			}

			public string Name {
				get { return _name; }
			}

			public bool EmitEnabled {
				get { return _emitEnabled; }
			}

			public bool TrackEmittedStreams {
				get { return _trackEmittedStreams; }
			}

			public int CheckpointAfterMs {
				get { return _checkpointAfterMs; }
			}

			public int CheckpointHandledThreshold {
				get { return _checkpointHandledThreshold; }
			}

			public int CheckpointUnhandledBytesThreshold {
				get { return _checkpointUnhandledBytesThreshold; }
			}

			public int PendingEventsThreshold {
				get { return _pendingEventsThreshold; }
			}

			public int MaxWriteBatchLength {
				get { return _maxWriteBatchLength; }
			}

			public int MaxAllowedWritesInFlight {
				get { return _maxAllowedWritesInFlight; }
			}

			public int? ProjectionExecutionTimeout { get => _projectionExecutionTimeout; }
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetStatistics : Message {
			private readonly IEnvelope _envelope;
			private readonly ProjectionMode? _mode;
			private readonly string _name;
			private readonly bool _includeDeleted;

			public GetStatistics(IEnvelope envelope, ProjectionMode? mode, string name, bool includeDeleted) {
				_envelope = envelope;
				_mode = mode;
				_name = name;
				_includeDeleted = includeDeleted;
			}

			public ProjectionMode? Mode {
				get { return _mode; }
			}

			public string Name {
				get { return _name; }
			}

			public bool IncludeDeleted {
				get { return _includeDeleted; }
			}

			public IEnvelope Envelope {
				get { return _envelope; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetState : Message {
			private readonly IEnvelope _envelope;
			private readonly string _name;
			private readonly string _partition;

			public GetState(IEnvelope envelope, string name, string partition) {
				if (envelope == null) throw new ArgumentNullException("envelope");
				if (name == null) throw new ArgumentNullException("name");
				if (partition == null) throw new ArgumentNullException("partition");
				_envelope = envelope;
				_name = name;
				_partition = partition;
			}

			public string Name {
				get { return _name; }
			}

			public IEnvelope Envelope {
				get { return _envelope; }
			}

			public string Partition {
				get { return _partition; }
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetResult : Message {
			private readonly IEnvelope _envelope;
			private readonly string _name;
			private readonly string _partition;

			public GetResult(IEnvelope envelope, string name, string partition) {
				if (envelope == null) throw new ArgumentNullException("envelope");
				if (name == null) throw new ArgumentNullException("name");
				if (partition == null) throw new ArgumentNullException("partition");
				_envelope = envelope;
				_name = name;
				_partition = partition;
			}

			public string Name {
				get { return _name; }
			}

			public IEnvelope Envelope {
				get { return _envelope; }
			}

			public string Partition {
				get { return _partition; }
			}
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class OperationFailed : Message {
		private readonly string _reason;

		public OperationFailed(string reason) {
			_reason = reason;
		}

		public string Reason {
			get { return _reason; }
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class NotFound : OperationFailed {
		public NotFound()
			: base("Not Found") {
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class NotAuthorized : OperationFailed {
		public NotAuthorized()
			: base("Not authorized") {
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class Conflict : OperationFailed {
		public Conflict(string reason)
			: base(reason) {
		}
	}

	public sealed class RunAs {
		private readonly ClaimsPrincipal _runAs;

		public RunAs(ClaimsPrincipal runAs) {
			_runAs = runAs;
		}

		private static readonly RunAs _anonymous = new RunAs(SystemAccounts.Anonymous);
		private static readonly RunAs _system = new RunAs(SystemAccounts.System);

		public static RunAs Anonymous {
			get { return _anonymous; }
		}

		public static RunAs System {
			get { return _system; }
		}

		public ClaimsPrincipal Principal {
			get { return _runAs; }
		}

		public static bool ValidateRunAs(ProjectionMode mode, ReadWrite readWrite, ClaimsPrincipal existingRunAs,
			Command.ControlMessage message, bool replace = false) {
			if (mode > ProjectionMode.Transient && readWrite == ReadWrite.Write
			                                    && (message.RunAs == null || message.RunAs.Principal == null
			                                                              || !(
																				   message.RunAs.Principal.LegacyRoleCheck(SystemRoles.Admins)
																		  		|| message.RunAs.Principal.LegacyRoleCheck(SystemRoles.Operations)
																			  ))) {
				message.Envelope.ReplyWith(new NotAuthorized());
				return false;
			}

			if (replace && message.RunAs.Principal == null) {
				message.Envelope.ReplyWith(new NotAuthorized());
				return false;
			}

			if (replace && message.RunAs.Principal != null)
				return true; // enable this operation while no projection permissions are defined

			return true;

			//if (existingRunAs == null)
			//    return true;
			//if (message.RunAs1.Principal == null
			//    || !string.Equals(
			//        existingRunAs.Identity.Name, message.RunAs1.Principal.Identity.Name,
			//        StringComparison.OrdinalIgnoreCase))
			//{
			//    message.Envelope.ReplyWith(new NotAuthorized());
			//    return false;
			//}
			//return true;
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class Updated : Message {
		private readonly string _name;

		public Updated(string name) {
			_name = name;
		}

		public string Name {
			get { return _name; }
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class Statistics : Message {
		private readonly ProjectionStatistics[] _projections;

		public Statistics(ProjectionStatistics[] projections) {
			_projections = projections;
		}

		public ProjectionStatistics[] Projections {
			get { return _projections; }
		}
	}

	[DerivedMessage]
	public abstract partial class ProjectionDataBase : Message {
		private readonly string _name;
		private readonly string _partition;
		private readonly CheckpointTag _position;
		private readonly Exception _exception;

		protected ProjectionDataBase(
			string name, string partition, CheckpointTag position, Exception exception = null) {
			_name = name;
			_partition = partition;
			_position = position;
			_exception = exception;
		}

		public string Name {
			get { return _name; }
		}

		public Exception Exception {
			get { return _exception; }
		}

		public string Partition {
			get { return _partition; }
		}

		public CheckpointTag Position {
			get { return _position; }
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionState : ProjectionDataBase {
		private readonly string _state;

		public ProjectionState(
			string name, string partition, string state, CheckpointTag position, Exception exception = null)
			: base(name, partition, position, exception) {
			_state = state;
		}

		public string State {
			get { return _state; }
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionResult : ProjectionDataBase {
		private readonly string _result;

		public ProjectionResult(
			string name, string partition, string result, CheckpointTag position, Exception exception = null)
			: base(name, partition, position, exception) {
			_result = result;
		}

		public string Result {
			get { return _result; }
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionQuery : Message {
		private readonly string _name;
		private readonly string _query;
		private readonly bool _emitEnabled;
		private readonly ProjectionSourceDefinition _definition;
		private readonly ProjectionOutputConfig _outputConfig;
		private readonly string _projectionType;
		private readonly bool? _trackEmittedStreams;
		private readonly bool? _checkpointsEnabled;

		public ProjectionQuery(
			string name,
			string query,
			bool emitEnabled,
			string projectionType,
			bool? trackEmittedStreams,
			bool? checkpointsEnabled,
			ProjectionSourceDefinition definition,
			ProjectionOutputConfig outputConfig) {
			_name = name;
			_query = query;
			_emitEnabled = emitEnabled;
			_projectionType = projectionType;
			_trackEmittedStreams = trackEmittedStreams;
			_checkpointsEnabled = checkpointsEnabled;
			_definition = definition;
			_outputConfig = outputConfig;
		}

		public string Name {
			get { return _name; }
		}

		public string Query {
			get { return _query; }
		}

		public bool EmitEnabled {
			get { return _emitEnabled; }
		}

		public bool? TrackEmittedStreams {
			get { return _trackEmittedStreams; }
		}

		public bool? CheckpointsEnabled {
			get { return _checkpointsEnabled; }
		}

		public string Type {
			get { return _projectionType; }
		}

		public ProjectionSourceDefinition Definition {
			get { return _definition; }
		}

		public ProjectionOutputConfig OutputConfig {
			get { return _outputConfig; }
		}
	}

	public static partial class Internal {
		[DerivedMessage(ProjectionMessage.Management)]
		public partial class CleanupExpired : Message {
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class ReadTimeout : Message {
			private readonly Guid _correlationId;
			private readonly string _streamId;
			private readonly Dictionary<string, object> _parameters;

			public Guid CorrelationId {
				get { return _correlationId; }
			}

			public string StreamId {
				get { return _streamId; }
			}

			public Dictionary<string, object> Parameters {
				get { return _parameters; }
			}

			public ReadTimeout(Guid correlationId, string streamId, Dictionary<string, object> parameters) {
				_correlationId = correlationId;
				_streamId = streamId;
				_parameters = parameters;
			}

			public ReadTimeout(Guid correlationId, string streamId) : this(correlationId, streamId,
				new Dictionary<string, object>()) {
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Deleted : Message {
			private readonly string _name;
			private readonly Guid _id;

			public Deleted(string name, Guid id) {
				_name = name;
				_id = id;
			}

			public string Name {
				get { return _name; }
			}

			public Guid Id {
				get { return _id; }
			}
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionConfig : Message {
		private readonly bool _emitEnabled;
		private readonly bool _trackEmittedStreams;
		private readonly int _checkpointAfterMs;
		private readonly int _checkpointHandledThreshold;
		private readonly int _checkpointUnhandledBytesThreshold;
		private readonly int _pendingEventsThreshold;
		private readonly int _maxWriteBatchLength;
		private readonly int _maxAllowedWritesInFlight;
		private readonly int? _projectionExecutionTimeout;

		public ProjectionConfig(bool emitEnabled, bool trackEmittedStreams, int checkpointAfterMs,
			int checkpointHandledThreshold,
			int checkpointUnhandledBytesThreshold, int pendingEventsThreshold, int maxWriteBatchLength,
			int maxAllowedWritesInFlight, int? projectionExecutionTimeout) {
			_emitEnabled = emitEnabled;
			_trackEmittedStreams = trackEmittedStreams;
			_checkpointAfterMs = checkpointAfterMs;
			_checkpointHandledThreshold = checkpointHandledThreshold;
			_checkpointUnhandledBytesThreshold = checkpointUnhandledBytesThreshold;
			_pendingEventsThreshold = pendingEventsThreshold;
			_maxWriteBatchLength = maxWriteBatchLength;
			_maxAllowedWritesInFlight = maxAllowedWritesInFlight;
			_projectionExecutionTimeout = projectionExecutionTimeout;
		}

		public bool EmitEnabled {
			get { return _emitEnabled; }
		}

		public bool TrackEmittedStreams {
			get { return _trackEmittedStreams; }
		}

		public int CheckpointAfterMs {
			get { return _checkpointAfterMs; }
		}

		public int CheckpointHandledThreshold {
			get { return _checkpointHandledThreshold; }
		}

		public int CheckpointUnhandledBytesThreshold {
			get { return _checkpointUnhandledBytesThreshold; }
		}

		public int PendingEventsThreshold {
			get { return _pendingEventsThreshold; }
		}

		public int MaxWriteBatchLength {
			get { return _maxWriteBatchLength; }
		}

		public int MaxAllowedWritesInFlight {
			get { return _maxAllowedWritesInFlight; }
		}

		public int? ProjectionExecutionTimeout { get => _projectionExecutionTimeout; }
	}
}
