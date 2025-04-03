// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public static class EventNumber {
	public const long DeletedStream = long.MaxValue;
	public const long Invalid = int.MinValue;
}
