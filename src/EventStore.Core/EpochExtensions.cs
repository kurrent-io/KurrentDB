// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core;

internal static class EpochExtensions {
	public static DateTime FromTicksSinceEpoch(this long value) =>
		new DateTime(DateTime.UnixEpoch.Ticks + value, DateTimeKind.Utc);

	public static long ToTicksSinceEpoch(this DateTime value) =>
		(value - DateTime.UnixEpoch).Ticks;
}
