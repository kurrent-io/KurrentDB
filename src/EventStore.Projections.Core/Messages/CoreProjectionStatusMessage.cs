// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Messages;

public static partial class CoreProjectionStatusMessage {
	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class CoreProjectionStatusMessageBase : CoreProjectionManagementMessageBase {
		protected CoreProjectionStatusMessageBase(Guid projectionId)
			: base(projectionId) {
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Started : CoreProjectionStatusMessageBase {
		public string Name { get; }
		public Started(Guid projectionId, string name)
			: base(projectionId) {
			Name = name;
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Faulted : CoreProjectionStatusMessageBase {
		private readonly string _faultedReason;

		public Faulted(Guid projectionId, string faultedReason)
			: base(projectionId) {
			_faultedReason = faultedReason;
		}

		public string FaultedReason {
			get { return _faultedReason; }
		}
	}

	[DerivedMessage]
	public abstract partial class DataReportBase : CoreProjectionStatusMessageBase {
		private readonly Guid _correlationId;
		private readonly string _partition;
		private readonly CheckpointTag _position;

		protected DataReportBase(Guid correlationId, Guid projectionId, string partition, CheckpointTag position)
			: base(projectionId) {
			_correlationId = correlationId;
			_partition = partition;
			_position = position;
		}

		public string Partition {
			get { return _partition; }
		}

		public Guid CorrelationId {
			get { return _correlationId; }
		}

		public CheckpointTag Position {
			get { return _position; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class StateReport : DataReportBase {
		private readonly string _state;

		public StateReport(
			Guid correlationId,
			Guid projectionId,
			string partition,
			string state,
			CheckpointTag position)
			: base(correlationId, projectionId, partition, position) {
			_state = state;
		}

		public string State {
			get { return _state; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class ResultReport : DataReportBase {
		private readonly string _result;

		public ResultReport(
			Guid correlationId,
			Guid projectionId,
			string partition,
			string result,
			CheckpointTag position)
			: base(correlationId, projectionId, partition, position) {
			_result = result;
		}

		public string Result {
			get { return _result; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class StatisticsReport : CoreProjectionStatusMessageBase {
		private readonly ProjectionStatistics _statistics;
		private readonly int _sequentialNumber;

		public StatisticsReport(Guid projectionId, ProjectionStatistics statistics, int sequentialNumber)
			: base(projectionId) {
			_statistics = statistics;
			_sequentialNumber = sequentialNumber;
		}

		public ProjectionStatistics Statistics {
			get { return _statistics; }
		}

		public int SequentialNumber {
			get { return _sequentialNumber; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Prepared : CoreProjectionStatusMessageBase {
		private readonly ProjectionSourceDefinition _sourceDefinition;

		public Prepared(Guid projectionId, ProjectionSourceDefinition sourceDefinition)
			: base(projectionId) {
			_sourceDefinition = sourceDefinition;
		}

		public ProjectionSourceDefinition SourceDefinition {
			get { return _sourceDefinition; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Suspended : CoreProjectionStatusMessageBase {
		public Suspended(Guid projectionId)
			: base(projectionId) {
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Stopped : CoreProjectionStatusMessageBase {
		private readonly bool _completed;
		private readonly string _name;

		public Stopped(Guid projectionId, string name, bool completed)
			: base(projectionId) {
			_completed = completed;
			_name = name;
		}

		public bool Completed {
			get { return _completed; }
		}

		public string Name {
			get { return _name; }
		}
	}
}
