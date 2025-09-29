// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;

namespace KurrentDB.Projections.Core.Messages;

public static partial class CoreProjectionManagementMessage {
	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Start(Guid projectionId, Guid workerId) : CoreProjectionManagementControlMessage(projectionId, workerId);

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class LoadStopped(Guid correlationId, Guid workerId) : CoreProjectionManagementControlMessage(correlationId, workerId);

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Stop(Guid projectionId, Guid workerId) : CoreProjectionManagementControlMessage(projectionId, workerId);

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Kill(Guid projectionId, Guid workerId) : CoreProjectionManagementControlMessage(projectionId, workerId);

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class GetState(Guid correlationId, Guid projectionId, string partition, Guid workerId)
		: CoreProjectionManagementControlMessage(projectionId, workerId) {
		public string Partition { get; } = Ensure.NotNull(partition);

		public Guid CorrelationId { get; } = correlationId;
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class GetResult(Guid correlationId, Guid projectionId, string partition, Guid workerId)
		: CoreProjectionManagementControlMessage(projectionId, workerId) {
		public string Partition { get; } = Ensure.NotNull(partition);

		public Guid CorrelationId { get; } = correlationId;
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class CreateAndPrepare(
		Guid projectionId,
		Guid workerId,
		string name,
		ProjectionVersion version,
		ProjectionConfig config,
		string handlerType,
		string query,
		bool enableContentTypeValidation)
		: CoreProjectionManagementControlMessage(projectionId, workerId) {
		public ProjectionConfig Config { get; } = config;
		public string Name { get; } = name;
		public ProjectionVersion Version { get; } = version;
		public string HandlerType { get; } = handlerType;
		public string Query { get; } = query;
		public bool EnableContentTypeValidation { get; } = enableContentTypeValidation;
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class CreatePrepared(
		Guid projectionId,
		Guid workerId,
		string name,
		ProjectionVersion version,
		ProjectionConfig config,
		QuerySourcesDefinition sourceDefinition,
		string handlerType,
		string query,
		bool enableContentTypeValidation)
		: CoreProjectionManagementControlMessage(projectionId, workerId) {
		public ProjectionConfig Config { get; } = Ensure.NotNull(config);
		public string Name { get; } = Ensure.NotNull(name);
		public QuerySourcesDefinition SourceDefinition { get; } = Ensure.NotNull(sourceDefinition);
		public ProjectionVersion Version { get; } = version;
		public string HandlerType { get; } = Ensure.NotNull(handlerType);
		public string Query { get; } = Ensure.NotNull(query);
		public bool EnableContentTypeValidation { get; } = enableContentTypeValidation;
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Dispose(Guid projectionId, Guid workerId) : CoreProjectionManagementControlMessage(projectionId, workerId);
}
