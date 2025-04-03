// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.Serialization;

namespace EventStore.Projections.Core.Services.Processing;

[DataContract]
public class QuerySourceOptions {
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

	protected bool Equals(QuerySourceOptions other) {
		return string.Equals(ResultStreamName, other.ResultStreamName)
		       && string.Equals(PartitionResultStreamNamePattern, other.PartitionResultStreamNamePattern)
		       && ReorderEvents.Equals(other.ReorderEvents) && ProcessingLag == other.ProcessingLag
		       && IsBiState.Equals(other.IsBiState) && DefinesStateTransform.Equals(other.DefinesStateTransform)
		       && ProducesResults.Equals(other.ProducesResults) && DefinesFold.Equals(other.DefinesFold)
		       && HandlesDeletedNotifications.Equals(other.HandlesDeletedNotifications)
		       && IncludeLinks.Equals(other.IncludeLinks);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj)) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != this.GetType()) return false;
		return Equals((QuerySourceOptions)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int hashCode = (ResultStreamName != null ? ResultStreamName.GetHashCode() : 0);
			hashCode = (hashCode * 397) ^ (PartitionResultStreamNamePattern != null
				? PartitionResultStreamNamePattern.GetHashCode()
				: 0);
			hashCode = (hashCode * 397) ^ ReorderEvents.GetHashCode();
			hashCode = (hashCode * 397) ^ ProcessingLag;
			hashCode = (hashCode * 397) ^ IsBiState.GetHashCode();
			hashCode = (hashCode * 397) ^ DefinesStateTransform.GetHashCode();
			hashCode = (hashCode * 397) ^ ProducesResults.GetHashCode();
			hashCode = (hashCode * 397) ^ DefinesFold.GetHashCode();
			hashCode = (hashCode * 397) ^ HandlesDeletedNotifications.GetHashCode();
			hashCode = (hashCode * 397) ^ IncludeLinks.GetHashCode();
			return hashCode;
		}
	}
}
