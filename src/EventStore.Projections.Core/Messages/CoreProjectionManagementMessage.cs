// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Messages;

public static partial class CoreProjectionManagementMessage {
	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Start : CoreProjectionManagementControlMessage {
		public Start(Guid projectionId, Guid workerId)
			: base(projectionId, workerId) {
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class LoadStopped : CoreProjectionManagementControlMessage {
		public LoadStopped(Guid correlationId, Guid workerId)
			: base(correlationId, workerId) {
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Stop : CoreProjectionManagementControlMessage {
		public Stop(Guid projectionId, Guid workerId)
			: base(projectionId, workerId) {
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Kill : CoreProjectionManagementControlMessage {
		public Kill(Guid projectionId, Guid workerId)
			: base(projectionId, workerId) {
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class GetState : CoreProjectionManagementControlMessage {
		private readonly Guid _correlationId;
		private readonly string _partition;

		public GetState(Guid correlationId, Guid projectionId, string partition, Guid workerId)
			: base(projectionId, workerId) {
			if (partition == null) throw new ArgumentNullException("partition");
			_correlationId = correlationId;
			_partition = partition;
		}

		public string Partition {
			get { return _partition; }
		}

		public Guid CorrelationId {
			get { return _correlationId; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class GetResult : CoreProjectionManagementControlMessage {
		private readonly Guid _correlationId;
		private readonly string _partition;

		public GetResult(Guid correlationId, Guid projectionId, string partition, Guid workerId)
			: base(projectionId, workerId) {
			if (partition == null) throw new ArgumentNullException("partition");
			_correlationId = correlationId;
			_partition = partition;
		}

		public string Partition {
			get { return _partition; }
		}

		public Guid CorrelationId {
			get { return _correlationId; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class CreateAndPrepare : CoreProjectionManagementControlMessage {
		private readonly ProjectionConfig _config;
		private readonly string _handlerType;
		private readonly string _query;
		private readonly string _name;
		private readonly ProjectionVersion _version;
		private readonly bool _enableContentTypeValidation;

		public CreateAndPrepare(
			Guid projectionId,
			Guid workerId,
			string name,
			ProjectionVersion version,
			ProjectionConfig config,
			string handlerType,
			string query,
			bool enableContentTypeValidation)
			: base(projectionId, workerId) {
			_name = name;
			_version = version;
			_config = config;
			_handlerType = handlerType;
			_query = query;
			_enableContentTypeValidation = enableContentTypeValidation;
		}

		public ProjectionConfig Config {
			get { return _config; }
		}

		public string Name {
			get { return _name; }
		}

		public ProjectionVersion Version {
			get { return _version; }
		}

		public string HandlerType {
			get { return _handlerType; }
		}

		public string Query {
			get { return _query; }
		}

		public bool EnableContentTypeValidation {
			get { return _enableContentTypeValidation; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class CreatePrepared : CoreProjectionManagementControlMessage {
		private readonly ProjectionConfig _config;
		private readonly QuerySourcesDefinition _sourceDefinition;
		private readonly string _handlerType;
		private readonly string _query;
		private readonly string _name;
		private readonly ProjectionVersion _version;
		private readonly bool _enableContentTypeValidation;

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
			if (name == null) throw new ArgumentNullException("name");
			if (config == null) throw new ArgumentNullException("config");
			if (sourceDefinition == null) throw new ArgumentNullException("sourceDefinition");
			if (handlerType == null) throw new ArgumentNullException("handlerType");
			if (query == null) throw new ArgumentNullException("query");
			_name = name;
			_version = version;
			_config = config;
			_sourceDefinition = sourceDefinition;
			_handlerType = handlerType;
			_query = query;
			_enableContentTypeValidation = enableContentTypeValidation;
		}

		public ProjectionConfig Config {
			get { return _config; }
		}

		public string Name {
			get { return _name; }
		}

		public QuerySourcesDefinition SourceDefinition {
			get { return _sourceDefinition; }
		}

		public ProjectionVersion Version {
			get { return _version; }
		}

		public string HandlerType {
			get { return _handlerType; }
		}

		public string Query {
			get { return _query; }
		}

		public bool EnableContentTypeValidation {
			get { return _enableContentTypeValidation; }
		}
	}

	[DerivedMessage(ProjectionMessage.CoreManagement)]
	public partial class Dispose : CoreProjectionManagementControlMessage {
		public Dispose(Guid projectionId, Guid workerId)
			: base(projectionId, workerId) {
		}
	}
}
