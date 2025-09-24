// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
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
	public partial class GetState : CoreProjectionManagementControlMessage {
		public GetState(Guid correlationId, Guid projectionId, string partition, Guid workerId) : base(projectionId, workerId) {
			ArgumentNullException.ThrowIfNull(partition);
			CorrelationId = correlationId;
			Partition = partition;
		}

		public string Partition { get; }
		public Guid CorrelationId { get; }
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class GetResult : CoreProjectionManagementControlMessage {
		public GetResult(Guid correlationId, Guid projectionId, string partition, Guid workerId) : base(projectionId, workerId) {
			ArgumentNullException.ThrowIfNull(partition);
			CorrelationId = correlationId;
			Partition = partition;
		}

		public string Partition { get; }
		public Guid CorrelationId { get; }
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
	public partial class CreatePrepared : CoreProjectionManagementControlMessage {
		public CreatePrepared(
			Guid projectionId,
			Guid workerId,
			string name,
			ProjectionVersion version,
			ProjectionConfig config,
			QuerySourcesDefinition sourceDefinition,
			string handlerType,
			string query,
			bool enableContentTypeValidation)
			: base(projectionId, workerId) {
			ArgumentNullException.ThrowIfNull(name);
			ArgumentNullException.ThrowIfNull(config);
			ArgumentNullException.ThrowIfNull(sourceDefinition);
			ArgumentNullException.ThrowIfNull(handlerType);
			ArgumentNullException.ThrowIfNull(query);
			Name = name;
			Version = version;
			Config = config;
			SourceDefinition = sourceDefinition;
			HandlerType = handlerType;
			Query = query;
			EnableContentTypeValidation = enableContentTypeValidation;
		}

		public ProjectionConfig Config { get; }
		public string Name { get; }
		public QuerySourcesDefinition SourceDefinition { get; }
		public ProjectionVersion Version { get; }
		public string HandlerType { get; }
		public string Query { get; }
		public bool EnableContentTypeValidation { get; }
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Dispose(Guid projectionId, Guid workerId) : CoreProjectionManagementControlMessage(projectionId, workerId);
}
