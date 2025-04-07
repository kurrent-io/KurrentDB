// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;

namespace EventStore.Projections.Core.Javascript.Tests.Integration;

public static class TestPollyFills {
	public static async Task WaitAsync(this Task toWaitFor, CancellationToken cancellationToken) {
		var tcs = new TaskCompletionSource();
		await using var reg = cancellationToken.Register(() => { tcs.TrySetCanceled(); });
		var result = await Task.WhenAny(tcs.Task, toWaitFor);
		await result;
	}
	public static async Task<T> WaitAsync<T>(this Task<T> toWaitFor, CancellationToken cancellationToken) {
		var tcs = new TaskCompletionSource<T>();
		await using var reg = cancellationToken.Register(() => { tcs.TrySetCanceled(); });
		var result = await Task.WhenAny(tcs.Task, toWaitFor);
		return await result;
	}

	public static Event[] LikeBeforeTheyWereSaved(this IReadOnlyList<ResolvedEvent> events) {
		return events.Select(x => new Event(x.Event.EventId, x.Event.EventType, x.Event.IsJson,
			x.Event.Data.ToArray(), x.Event.Metadata.ToArray())).ToArray();
	}
}
