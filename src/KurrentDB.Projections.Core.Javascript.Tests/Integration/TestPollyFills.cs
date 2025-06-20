// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;

namespace KurrentDB.Projections.Core.Javascript.Tests.Integration;

public static class TestPollyFills {
	public static async Task WaitAsync(this Task toWaitFor, CancellationToken cancellationToken) {
		var tcs = TaskCompletionSourceFactory.CreateDefault();
		await using var reg = cancellationToken.Register(() => { tcs.TrySetCanceled(); });
		var result = await Task.WhenAny(tcs.Task, toWaitFor);
		await result;
	}
	public static async Task<T> WaitAsync<T>(this Task<T> toWaitFor, CancellationToken cancellationToken) {
		var tcs = TaskCompletionSourceFactory.CreateDefault<T>();
		await using var reg = cancellationToken.Register(() => { tcs.TrySetCanceled(); });
		var result = await Task.WhenAny(tcs.Task, toWaitFor);
		return await result;
	}

	public static Event[] LikeBeforeTheyWereSaved(this IReadOnlyList<ResolvedEvent> events) {
		return events.Select(x => new Event(x.Event.EventId, x.Event.EventType, x.Event.IsJson,
			x.Event.Data.ToArray(), x.Event.Metadata.ToArray(), x.Event.Properties.ToArray())).ToArray();
	}
}
