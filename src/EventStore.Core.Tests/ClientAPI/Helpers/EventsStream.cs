// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.ClientAPI;

namespace EventStore.Core.Tests.ClientAPI.Helpers;

internal class EventsStream {
	private const int SliceSize = 10;

	public static async Task<int> Count(IEventStoreConnection store, string stream) {
		var result = 0;
		while (true) {
			var slice = await store.ReadStreamEventsForwardAsync(stream, result, SliceSize, false);
			result += slice.Events.Length;
			if (slice.IsEndOfStream)
				break;
		}

		return result;
	}
}
