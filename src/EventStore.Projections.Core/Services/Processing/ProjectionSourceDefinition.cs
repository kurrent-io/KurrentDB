// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Runtime.Serialization;
using EventStore.Projections.Core.Messages;

namespace EventStore.Projections.Core.Services.Processing;

[DataContract]
public class ProjectionSourceDefinition : IQuerySources {
	[DataMember] public bool AllEvents { get; set; }

	[DataMember] public bool AllStreams { get; set; }

	[DataMember] public bool ByStream { get; set; }

	[DataMember] public bool ByCustomPartitions { get; set; }

	[DataMember] public string[] Categories { get; set; }

	[DataMember] public string[] Events { get; set; }

	[DataMember] public string[] Streams { get; set; }

	[DataMember] public long? LimitingCommitPosition { get; set; }

	[DataMember] public QuerySourceOptions Options { get; set; }

	bool IQuerySources.DefinesStateTransform {
		get { return Options != null && Options.DefinesStateTransform; }
	}

	bool IQuerySources.ProducesResults {
		get { return Options != null && Options.ProducesResults; }
	}

	bool IQuerySources.DefinesFold {
		get { return Options != null && Options.DefinesFold; }
	}

	bool IQuerySources.HandlesDeletedNotifications {
		get { return Options != null && Options.HandlesDeletedNotifications; }
	}

	bool IQuerySources.IncludeLinksOption {
		get { return Options != null && Options.IncludeLinks; }
	}

	string IQuerySources.ResultStreamNameOption {
		get { return Options != null ? Options.ResultStreamName : null; }
	}

	string IQuerySources.PartitionResultStreamNamePatternOption {
		get { return Options != null ? Options.PartitionResultStreamNamePattern : null; }
	}

	bool IQuerySources.ReorderEventsOption {
		get { return Options != null && Options.ReorderEvents; }
	}

	int? IQuerySources.ProcessingLagOption {
		get { return Options != null ? Options.ProcessingLag : (int?)null; }
	}

	bool IQuerySources.IsBiState {
		get { return Options != null ? Options.IsBiState : false; }
	}

	bool IQuerySources.ByStreams {
		get { return ByStream; }
	}

	public static ProjectionSourceDefinition From(IQuerySources sources) {
		return new ProjectionSourceDefinition {
			AllEvents = sources.AllEvents,
			AllStreams = sources.AllStreams,
			ByStream = sources.ByStreams,
			ByCustomPartitions = sources.ByCustomPartitions,
			Categories = (sources.Categories ?? new string[0]).ToArray(),
			Events = (sources.Events ?? new string[0]).ToArray(),
			Streams = (sources.Streams ?? new string[0]).ToArray(),
			LimitingCommitPosition = sources.LimitingCommitPosition,
			Options =
				new QuerySourceOptions {
					DefinesStateTransform = sources.DefinesStateTransform,
					ProducesResults = sources.ProducesResults,
					DefinesFold = sources.DefinesFold,
					HandlesDeletedNotifications = sources.HandlesDeletedNotifications,
					IncludeLinks = sources.IncludeLinksOption,
					PartitionResultStreamNamePattern = sources.PartitionResultStreamNamePatternOption,
					ProcessingLag = sources.ProcessingLagOption.GetValueOrDefault(),
					IsBiState = sources.IsBiState,
					ReorderEvents = sources.ReorderEventsOption,
					ResultStreamName = sources.ResultStreamNameOption,
				},
		};
	}

	private bool Equals(string[] a, string[] b) {
		bool aEmpty = (a == null || a.Length == 0);
		bool bEmpty = (b == null || b.Length == 0);
		if (aEmpty && bEmpty)
			return true;
		if (aEmpty || bEmpty)
			return false;
		return a.SequenceEqual(b);
	}

	protected bool Equals(ProjectionSourceDefinition other) {
		return AllEvents.Equals(other.AllEvents) && AllStreams.Equals(other.AllStreams)
		                                         && ByStream.Equals(other.ByStream) &&
		                                         ByCustomPartitions.Equals(other.ByCustomPartitions)
		                                         && Equals(Categories, other.Categories) &&
		                                         Equals(Events, other.Events)
		                                         && Equals(Streams, other.Streams)
		                                         && LimitingCommitPosition == other.LimitingCommitPosition &&
		                                         Equals(Options, other.Options);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj)) return false;
		if (ReferenceEquals(this, obj)) return true;
		if (obj.GetType() != this.GetType()) return false;
		return Equals((ProjectionSourceDefinition)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int hashCode = AllEvents.GetHashCode();
			hashCode = (hashCode * 397) ^ AllStreams.GetHashCode();
			hashCode = (hashCode * 397) ^ ByStream.GetHashCode();
			hashCode = (hashCode * 397) ^ ByCustomPartitions.GetHashCode();
			hashCode = (hashCode * 397) ^ LimitingCommitPosition.GetHashCode();
			hashCode = (hashCode * 397) ^ (Options != null ? Options.GetHashCode() : 0);
			return hashCode;
		}
	}
}
