// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.Serialization;

namespace EventStore.Projections.Core.Messages;

[DataContract]
public class QuerySourcesDefinitionOptions {
	[DataMember(Name = "producesResults")] public bool ProducesResults { get; set; }

	[DataMember(Name = "definesFold")] public bool DefinesFold { get; set; }

	[DataMember(Name = "handlesDeletedNotifications")]
	public bool HandlesDeletedNotifications { get; set; }

	[DataMember(Name = "definesStateTransform")]
	public bool DefinesStateTransform { get; set; }

	[DataMember(Name = "resultStreamName")]
	public string ResultStreamName { get; set; }

	[DataMember(Name = "partitionResultStreamNamePattern")]
	public string PartitionResultStreamNamePattern { get; set; }

	[DataMember(Name = "$includeLinks")] public bool IncludeLinks { get; set; }

	[DataMember(Name = "reorderEvents")] public bool ReorderEvents { get; set; }

	[DataMember(Name = "processingLag")] public int? ProcessingLag { get; set; }

	[DataMember(Name = "biState")] public bool IsBiState { get; set; }
}
