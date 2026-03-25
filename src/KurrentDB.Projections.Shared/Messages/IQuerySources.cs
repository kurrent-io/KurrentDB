// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Messages;

public interface IQuerySources {
	// Stream source — mutually exclusive: fromAll(), fromCategory(), or fromStream()/fromStreams()
	// Note: fromCategories() and multi-arg fromCategory() map to Streams (as $ce- prefixed), not Categories
	bool AllStreams { get; }

	string[] Categories { get; }

	string[] Streams { get; }

	// Event type filter — mutually exclusive: all events or specific event types
	bool AllEvents { get; }

	string[] Events { get; }

	// Partitioning — mutually exclusive: foreachStream(), partitionBy(), or unpartitioned
	bool ByStreams { get; }

	bool ByCustomPartitions { get; }

	// Processing capabilities — independent flags
	bool DefinesStateTransform { get; }

	bool DefinesFold { get; }

	bool HandlesDeletedNotifications { get; }

	bool ProducesResults { get; }

	bool IsBiState { get; } // requires partitioning

	// Output options
	bool IncludeLinksOption { get; }

	string ResultStreamNameOption { get; }

	string PartitionResultStreamNamePatternOption { get; }

	// Ordering options
	bool ReorderEventsOption { get; }

	int? ProcessingLagOption { get; }
}

public static class QuerySourcesExtensions {
	public static bool HasStreams(this IQuerySources sources) {
		var streams = sources.Streams;
		return streams != null && streams.Length > 0;
	}

	public static bool HasCategories(this IQuerySources sources) {
		var categories = sources.Categories;
		return categories != null && categories.Length > 0;
	}

	public static bool HasEvents(this IQuerySources sources) {
		var events = sources.Events;
		return events != null && events.Length > 0;
	}
}
