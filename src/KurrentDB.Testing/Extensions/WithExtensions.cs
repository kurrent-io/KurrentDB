// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

namespace KurrentDB.Testing;

public static class WithExtensions {
	[DebuggerStepThrough]
	public static T With<T>(this T instance, Action<T> update) {
		update(instance);
		return instance;
	}

	[DebuggerStepThrough]
	public static T With<T>(this T instance, Func<T, T> update) {
        update(instance);
        return instance;
    }
}
