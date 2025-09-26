// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Runtime.Serialization;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing;

[DataContract]
public class ProjectionSourceDefinition : IQuerySources {
	[DataMember] public bool AllEvents { get; set; }

	[DataMember] public bool AllStreams { get; set; }

	[DataMember] public bool ByStream { get; set; }

	[DataMember] public bool ByCustomPartitions { get; set; }

	[DataMember] public string[] Categories { get; set; }

	[DataMember] public string[] Events { get; set; }

	[DataMember] public string[] Streams { get; set; }

	[DataMember] public QuerySourceOptions Options { get; set; }

	bool IQuerySources.DefinesStateTransform => Options is { DefinesStateTransform: true };

	bool IQuerySources.ProducesResults => Options is { ProducesResults: true };

	bool IQuerySources.DefinesFold => Options is { DefinesFold: true };

	bool IQuerySources.HandlesDeletedNotifications => Options is { HandlesDeletedNotifications: true };

	bool IQuerySources.IncludeLinksOption => Options is { IncludeLinks: true };

	string IQuerySources.ResultStreamNameOption => Options?.ResultStreamName;

	string IQuerySources.PartitionResultStreamNamePatternOption => Options?.PartitionResultStreamNamePattern;

	bool IQuerySources.ReorderEventsOption => Options is { ReorderEvents: true };

	int? IQuerySources.ProcessingLagOption => Options?.ProcessingLag;

	bool IQuerySources.IsBiState => Options is { IsBiState : true };

	bool IQuerySources.ByStreams => ByStream;

	public static ProjectionSourceDefinition From(IQuerySources sources) {
		return new ProjectionSourceDefinition {
			AllEvents = sources.AllEvents,
			AllStreams = sources.AllStreams,
			ByStream = sources.ByStreams,
			ByCustomPartitions = sources.ByCustomPartitions,
			Categories = (sources.Categories ?? []).ToArray(),
			Events = (sources.Events ?? []).ToArray(),
			Streams = (sources.Streams ?? []).ToArray(),
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

	private static bool Equals(string[] a, string[] b) {
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
		                                         && Equals(Options, other.Options);
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj))
			return false;
		if (ReferenceEquals(this, obj))
			return true;
		if (obj.GetType() != this.GetType())
			return false;
		return Equals((ProjectionSourceDefinition)obj);
	}

	public override int GetHashCode() {
		unchecked {
			int hashCode = AllEvents.GetHashCode();
			hashCode = (hashCode * 397) ^ AllStreams.GetHashCode();
			hashCode = (hashCode * 397) ^ ByStream.GetHashCode();
			hashCode = (hashCode * 397) ^ ByCustomPartitions.GetHashCode();
			hashCode = (hashCode * 397) ^ (Options != null ? Options.GetHashCode() : 0);
			return hashCode;
		}
	}
}
