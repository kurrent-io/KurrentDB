// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

#nullable enable

using System;
using EventStore.Core.Time;

namespace EventStore.Core.Util;

public static class Functions {
	public static Func<T> Debounce<T>(this Func<T> func, TimeSpan timeout, IClock? clock = null) {
		clock ??= Clock.Instance;
		var seconds = timeout.TotalSeconds;
		Instant lastCalled = default;
		T? current = default;

		return () => {
			var now = clock.Now;
			if (lastCalled == default || now.ElapsedSecondsSince(lastCalled) > seconds) {
				current = func();
				lastCalled = now;
			}

			return current!;
		};
	}
}
