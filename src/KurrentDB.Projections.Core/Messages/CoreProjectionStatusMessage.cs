// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Messages;

public static partial class CoreProjectionStatusMessage {
	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class CoreProjectionStatusMessageBase : CoreProjectionManagementMessageBase {
		protected CoreProjectionStatusMessageBase(Guid projectionId) : base(projectionId) {
		}
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Started(Guid projectionId, string name) : CoreProjectionStatusMessageBase(projectionId) {
		public string Name { get; } = name;
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Faulted(Guid projectionId, string faultedReason) : CoreProjectionStatusMessageBase(projectionId) {
		public string FaultedReason { get; } = faultedReason;
	}

	[DerivedMessage]
	public abstract partial class DataReportBase(Guid correlationId, Guid projectionId, string partition, CheckpointTag position)
		: CoreProjectionStatusMessageBase(projectionId) {
		public string Partition { get; } = partition;
		public Guid CorrelationId { get; } = correlationId;
		public CheckpointTag Position { get; } = position;
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class StateReport(
		Guid correlationId,
		Guid projectionId,
		string partition,
		string state,
		CheckpointTag position)
		: DataReportBase(correlationId, projectionId, partition, position) {
		public string State { get; } = state;
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class ResultReport(
		Guid correlationId,
		Guid projectionId,
		string partition,
		string result,
		CheckpointTag position)
		: DataReportBase(correlationId, projectionId, partition, position) {
		public string Result { get; } = result;
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class StatisticsReport(Guid projectionId, ProjectionStatistics statistics, int sequentialNumber)
		: CoreProjectionStatusMessageBase(projectionId) {
		public ProjectionStatistics Statistics { get; } = statistics;
		[UsedImplicitly] public int SequentialNumber { get; } = sequentialNumber;
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Prepared(Guid projectionId, ProjectionSourceDefinition sourceDefinition)
		: CoreProjectionStatusMessageBase(projectionId) {
		public ProjectionSourceDefinition SourceDefinition { get; } = sourceDefinition;
	}

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Suspended(Guid projectionId) : CoreProjectionStatusMessageBase(projectionId);

	[DerivedMessage(ProjectionMessage.CoreStatus)]
	public partial class Stopped(Guid projectionId, string name, bool completed) : CoreProjectionStatusMessageBase(projectionId) {
		public bool Completed { get; } = completed;
		public string Name { get; } = name;
	}
}
