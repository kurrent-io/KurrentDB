// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Security.Claims;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Services;
using KurrentDB.Projections.Core.Messages;
using CoreEventFilter = KurrentDB.Core.Services.Storage.ReaderIndex.EventFilter;

namespace KurrentDB.Projections.Core.Services.Processing.V2.ReadStrategies;

public static class ReadStrategyFactory {
	public static IReadStrategy Create(IQuerySources sources, IPublisher bus, ClaimsPrincipal user) {
		if (sources.AllStreams) {
			var filter = sources.HasEvents()
				? CoreEventFilter.EventType.Prefixes(isAllStream: true, GetEventTypePrefixes(sources))
				: CoreEventFilter.DefaultAllFilter;

			return new FilteredAllReadStrategy(bus, filter, user);
		}

		if (sources.HasStreams()) {
			var filter = CoreEventFilter.StreamName.Set(isAllStream: true, sources.Streams);
			return new FilteredAllReadStrategy(bus, filter, user);
		}

		if (sources.HasCategories()) {
			var prefixes = sources.Categories
				.Select(c => c + "-")
				.ToArray();

			var filter = CoreEventFilter.StreamName.Prefixes(isAllStream: true, prefixes);
			return new FilteredAllReadStrategy(bus, filter, user);
		}

		throw new ArgumentException("Unsupported query source combination: must specify AllStreams, Streams, or Categories.");
	}

	private static string[] GetEventTypePrefixes(IQuerySources sources) {
		if (!sources.HandlesDeletedNotifications)
			return sources.Events;

		// When the projection handles $deleted, we need tombstone events to pass through
		// the read-level filter. Hard delete writes $streamDeleted on the original stream;
		// soft delete writes $streamMetadata on $$stream with TruncateBefore=DeletedStream.
		return sources.Events
			.Append(SystemEventTypes.StreamDeleted)
			.Append(SystemEventTypes.StreamMetadata) // "$metadata" — written to $$stream on soft delete
			.ToArray();
	}
}
