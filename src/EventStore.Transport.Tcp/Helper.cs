// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;

namespace EventStore.Transport.Tcp;

internal static class Helper {
	public static void EatException(Action action) {
		try {
			action();
		} catch (Exception) {
		}
	}

	public static T EatException<T>(Func<T> func, T defaultValue = default(T)) {
		Ensure.NotNull(func, "func");
		try {
			return func();
		} catch (Exception) {
			return defaultValue;
		}
	}
}
