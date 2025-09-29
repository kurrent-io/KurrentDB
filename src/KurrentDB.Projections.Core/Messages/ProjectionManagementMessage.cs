// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.UserManagement;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Messages;

public static partial class ProjectionManagementMessage {
	public static partial class Command {
		[DerivedMessage]
		public abstract partial class ControlMessage(IEnvelope envelope, RunAs runAs) : Message {
			public readonly RunAs RunAs = runAs;

			public IEnvelope Envelope { get; } = envelope;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class PostBatch(IEnvelope envelope, RunAs runAs, PostBatch.ProjectionPost[] projections)
			: ControlMessage(envelope, runAs) {
			public ProjectionPost[] Projections { get; } = projections;

			public class ProjectionPost(
				ProjectionMode mode,
				RunAs runAs,
				string name,
				string handlerType,
				string query,
				bool enabled,
				bool checkpointsEnabled,
				bool emitEnabled,
				bool enableRunAs,
				bool trackEmittedStreams) {
				public ProjectionMode Mode { get; } = mode;
				public RunAs RunAs { get; } = runAs;
				public string Name { get; } = name;
				public string HandlerType { get; } = handlerType;
				public string Query { get; } = query;
				public bool Enabled { get; } = enabled;
				public bool CheckpointsEnabled { get; } = checkpointsEnabled;
				public bool EmitEnabled { get; } = emitEnabled;
				public bool EnableRunAs { get; } = enableRunAs;
				public bool TrackEmittedStreams { get; } = trackEmittedStreams;
			}
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Post : ControlMessage {
			public Post(
				IEnvelope envelope,
				ProjectionMode mode,
				string name,
				RunAs runAs,
				string handlerType,
				string query,
				bool enabled,
				bool checkpointsEnabled,
				bool emitEnabled,
				bool trackEmittedStreams,
				bool enableRunAs = false)
				: base(envelope, runAs) {
				Name = name;
				HandlerType = handlerType;
				Mode = mode;
				Query = query;
				Enabled = enabled;
				CheckpointsEnabled = checkpointsEnabled;
				EmitEnabled = emitEnabled;
				TrackEmittedStreams = trackEmittedStreams;
				EnableRunAs = enableRunAs;
			}

			public Post(
				IEnvelope envelope,
				ProjectionMode mode,
				string name,
				RunAs runAs,
				Type handlerType,
				string query,
				bool enabled,
				bool checkpointsEnabled,
				bool emitEnabled,
				bool trackEmittedStreams,
				bool enableRunAs = false)
				: base(envelope, runAs) {
				Name = name;
				HandlerType = $"native:{handlerType.Namespace}.{handlerType.Name}";
				Mode = mode;
				Query = query;
				Enabled = enabled;
				CheckpointsEnabled = checkpointsEnabled;
				EmitEnabled = emitEnabled;
				TrackEmittedStreams = trackEmittedStreams;
				EnableRunAs = enableRunAs;
			}

			// shortcut for posting ad-hoc JS queries
			public Post(IEnvelope envelope, RunAs runAs, string query, bool enabled)
				: base(envelope, runAs) {
				Name = Guid.NewGuid().ToString("D");
				HandlerType = "JS";
				Mode = ProjectionMode.Transient;
				Query = query;
				Enabled = enabled;
				CheckpointsEnabled = false;
				EmitEnabled = false;
				TrackEmittedStreams = false;
			}

			public ProjectionMode Mode { get; }
			public string Query { get; }
			public string Name { get; }
			public string HandlerType { get; }
			public bool Enabled { get; }
			public bool EmitEnabled { get; }
			public bool CheckpointsEnabled { get; }
			public bool EnableRunAs { get; }
			public bool TrackEmittedStreams { get; }
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Disable(IEnvelope envelope, string name, RunAs runAs) : ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Enable(IEnvelope envelope, string name, RunAs runAs) : ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Abort(IEnvelope envelope, string name, RunAs runAs) : ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class UpdateQuery(IEnvelope envelope, string name, RunAs runAs, string query, bool? emitEnabled)
			: ControlMessage(envelope, runAs) {
			public string Query { get; } = query;
			public string Name { get; } = name;
			public bool? EmitEnabled { get; } = emitEnabled;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Reset(IEnvelope envelope, string name, RunAs runAs) : ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Delete(
			IEnvelope envelope,
			string name,
			RunAs runAs,
			bool deleteCheckpointStream,
			bool deleteStateStream,
			bool deleteEmittedStreams)
			: ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
			public bool DeleteCheckpointStream { get; } = deleteCheckpointStream;
			public bool DeleteStateStream { get; } = deleteStateStream;
			public bool DeleteEmittedStreams { get; } = deleteEmittedStreams;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetQuery(IEnvelope envelope, string name, RunAs runAs) : ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetConfig(IEnvelope envelope, string name, RunAs runAs) : ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class UpdateConfig(
			IEnvelope envelope,
			string name,
			bool emitEnabled,
			bool trackEmittedStreams,
			int checkpointAfterMs,
			int checkpointHandledThreshold,
			int checkpointUnhandledBytesThreshold,
			int pendingEventsThreshold,
			int maxWriteBatchLength,
			int maxAllowedWritesInFlight,
			RunAs runAs,
			int? projectionExecutionTimeout)
			: ControlMessage(envelope, runAs) {
			public string Name { get; } = name;
			public bool EmitEnabled { get; } = emitEnabled;
			public bool TrackEmittedStreams { get; } = trackEmittedStreams;
			public int CheckpointAfterMs { get; } = checkpointAfterMs;
			public int CheckpointHandledThreshold { get; } = checkpointHandledThreshold;
			public int CheckpointUnhandledBytesThreshold { get; } = checkpointUnhandledBytesThreshold;
			public int PendingEventsThreshold { get; } = pendingEventsThreshold;
			public int MaxWriteBatchLength { get; } = maxWriteBatchLength;
			public int MaxAllowedWritesInFlight { get; } = maxAllowedWritesInFlight;
			public int? ProjectionExecutionTimeout { get; } = projectionExecutionTimeout;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetStatistics(IEnvelope envelope, ProjectionMode? mode, string name) : Message {
			public ProjectionMode? Mode { get; } = mode;
			public string Name { get; } = name;
			public IEnvelope Envelope { get; } = envelope;
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetState(IEnvelope envelope, string name, string partition) : Message {
			public string Name { get; } = Ensure.NotNull(name);
			public IEnvelope Envelope { get; } = Ensure.NotNull(envelope);
			public string Partition { get; } = Ensure.NotNull(partition);
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class GetResult(IEnvelope envelope, string name, string partition) : Message {
			public string Name { get; } = Ensure.NotNull(name);
			public IEnvelope Envelope { get; } = Ensure.NotNull(envelope);
			public string Partition { get; } = Ensure.NotNull(partition);
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class OperationFailed(string reason) : Message {
		public string Reason { get; } = reason;
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class NotFound() : OperationFailed("Not Found");

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class NotAuthorized() : OperationFailed("Not authorized");

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class Conflict(string reason) : OperationFailed(reason);

	public sealed class RunAs(ClaimsPrincipal runAs) {
		public static RunAs Anonymous { get; } = new(SystemAccounts.Anonymous);

		public static RunAs System { get; } = new(SystemAccounts.System);

		public ClaimsPrincipal Principal { get; } = runAs;

		public static bool ValidateRunAs(ProjectionMode mode, ReadWrite readWrite, Command.ControlMessage message, bool replace = false) {
			if (mode > ProjectionMode.Transient && readWrite == ReadWrite.Write
			                                    && (message.RunAs?.Principal == null
			                                        || !(message.RunAs.Principal.LegacyRoleCheck(SystemRoles.Admins)
			                                             || message.RunAs.Principal.LegacyRoleCheck(SystemRoles.Operations)
				                                        )) || replace && message.RunAs.Principal == null) {
				message.Envelope.ReplyWith(new NotAuthorized());
				return false;
			}

			return true;
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class Updated(string name) : Message {
		public string Name { get; } = name;
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class Statistics(ProjectionStatistics[] projections) : Message {
		public ProjectionStatistics[] Projections { get; } = projections;
	}

	[DerivedMessage]
	public abstract partial class ProjectionDataBase(string name, string partition, CheckpointTag position, Exception exception = null)
		: Message {
		public string Name { get; } = name;
		public Exception Exception { get; } = exception;
		public string Partition { get; } = partition;
		public CheckpointTag Position { get; } = position;
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionState(string name, string partition, string state, CheckpointTag position, Exception exception = null)
		: ProjectionDataBase(name, partition, position, exception) {
		public string State { get; } = state;
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionResult(string name, string partition, string result, CheckpointTag position, Exception exception = null)
		: ProjectionDataBase(name, partition, position, exception) {
		public string Result { get; } = result;
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionQuery(
		string name,
		string query,
		bool emitEnabled,
		string projectionType,
		bool? trackEmittedStreams,
		bool? checkpointsEnabled,
		ProjectionSourceDefinition definition,
		ProjectionOutputConfig outputConfig)
		: Message {
		public string Name { get; } = name;
		public string Query { get; } = query;
		public bool EmitEnabled { get; } = emitEnabled;
		public bool? TrackEmittedStreams { get; } = trackEmittedStreams;
		public bool? CheckpointsEnabled { get; } = checkpointsEnabled;
		public string Type { get; } = projectionType;
		public ProjectionSourceDefinition Definition { get; } = definition;
		public ProjectionOutputConfig OutputConfig { get; } = outputConfig;
	}

	public static partial class Internal {
		[DerivedMessage(ProjectionMessage.Management)]
		public partial class CleanupExpired : Message;

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class ReadTimeout(Guid correlationId, string streamId, Dictionary<string, object> parameters)
			: Message {
			public Guid CorrelationId { get; } = correlationId;
			public string StreamId { get; } = streamId;
			public Dictionary<string, object> Parameters { get; } = parameters;

			public ReadTimeout(Guid correlationId, string streamId) : this(correlationId, streamId, new()) { }
		}

		[DerivedMessage(ProjectionMessage.Management)]
		public partial class Deleted(string name, Guid id) : Message {
			public string Name { get; } = name;
			public Guid Id { get; } = id;
		}
	}

	[DerivedMessage(ProjectionMessage.Management)]
	public partial class ProjectionConfig(
		bool emitEnabled,
		bool trackEmittedStreams,
		int checkpointAfterMs,
		int checkpointHandledThreshold,
		int checkpointUnhandledBytesThreshold,
		int pendingEventsThreshold,
		int maxWriteBatchLength,
		int maxAllowedWritesInFlight,
		int? projectionExecutionTimeout)
		: Message {
		public bool EmitEnabled { get; } = emitEnabled;
		public bool TrackEmittedStreams { get; } = trackEmittedStreams;
		public int CheckpointAfterMs { get; } = checkpointAfterMs;
		public int CheckpointHandledThreshold { get; } = checkpointHandledThreshold;
		public int CheckpointUnhandledBytesThreshold { get; } = checkpointUnhandledBytesThreshold;
		public int PendingEventsThreshold { get; } = pendingEventsThreshold;
		public int MaxWriteBatchLength { get; } = maxWriteBatchLength;
		public int MaxAllowedWritesInFlight { get; } = maxAllowedWritesInFlight;
		public int? ProjectionExecutionTimeout { get; } = projectionExecutionTimeout;
	}
}
