// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.Serialization;

namespace KurrentDB.Projections.Core.Services.Processing;

[DataContract]
public record QuerySourceOptions {
	[DataMember] public string ResultStreamName { get; set; }
	[DataMember] public string PartitionResultStreamNamePattern { get; set; }
	[DataMember] public bool ReorderEvents { get; set; }
	[DataMember] public int ProcessingLag { get; set; }
	[DataMember] public bool IsBiState { get; set; }
	[DataMember] public bool DefinesStateTransform { get; set; }
	[DataMember] public bool ProducesResults { get; set; }
	[DataMember] public bool DefinesFold { get; set; }
	[DataMember] public bool HandlesDeletedNotifications { get; set; }
	[DataMember] public bool IncludeLinks { get; set; }
}
