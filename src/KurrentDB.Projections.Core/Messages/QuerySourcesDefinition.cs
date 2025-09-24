// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Runtime.Serialization;

namespace KurrentDB.Projections.Core.Messages;

[DataContract]
public class QuerySourcesDefinition : IQuerySources {
	[DataMember(Name = "allStreams")] public bool AllStreams { get; set; }

	[DataMember(Name = "categories")] public string[] Categories { get; set; }

	[DataMember(Name = "streams")] public string[] Streams { get; set; }

	[DataMember(Name = "allEvents")] public bool AllEvents { get; set; }

	[DataMember(Name = "events")] public string[] Events { get; set; }

	[DataMember(Name = "byStreams")] public bool ByStreams { get; set; }

	[DataMember(Name = "byCustomPartitions")]
	public bool ByCustomPartitions { get; set; }

	bool IQuerySources.DefinesStateTransform => Options is { DefinesStateTransform: true };

	bool IQuerySources.ProducesResults => Options is { ProducesResults: true };

	bool IQuerySources.DefinesFold => Options is { DefinesFold: true };

	bool IQuerySources.HandlesDeletedNotifications => Options is { HandlesDeletedNotifications: true };

	bool IQuerySources.IncludeLinksOption => Options is { IncludeLinks: true };

	string IQuerySources.ResultStreamNameOption => Options?.ResultStreamName;

	string IQuerySources.PartitionResultStreamNamePatternOption => Options?.PartitionResultStreamNamePattern;

	bool IQuerySources.ReorderEventsOption => Options is { ReorderEvents: true };

	int? IQuerySources.ProcessingLagOption => Options?.ProcessingLag;

	bool IQuerySources.IsBiState => Options?.IsBiState ?? false;

	[DataMember(Name = "options")] public QuerySourcesDefinitionOptions Options { get; set; }

	public static QuerySourcesDefinition From(IQuerySources sources) => new() {
		AllEvents = sources.AllEvents,
		AllStreams = sources.AllStreams,
		ByStreams = sources.ByStreams,
		ByCustomPartitions = sources.ByCustomPartitions,
		Categories = (sources.Categories ?? []).ToArray(),
		Events = (sources.Events ?? []).ToArray(),
		Streams = (sources.Streams ?? []).ToArray(),
		Options =
			new QuerySourcesDefinitionOptions {
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
			}
	};
}
