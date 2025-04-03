// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using EventStore.Core.Time;

namespace EventStore.Core.Util;

public static class Functions {
    public static Func<T> Debounce<T>(this Func<T> func, TimeSpan timeout, IClock? clock = null) {
        clock ??= Clock.Instance;
        var     seconds    = timeout.TotalSeconds;
        Instant lastCalled = default;
        T?      current    = default;

        return () => {
            var now = clock.Now;
            if (lastCalled == default || now.ElapsedSecondsSince(lastCalled) > seconds) {
                current    = func();
                lastCalled = now;
            }

            return current!;
        };
    }
}
