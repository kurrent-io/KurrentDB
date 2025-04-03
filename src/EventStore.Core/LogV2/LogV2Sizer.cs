// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Caching;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV2;

public class LogV2Sizer : ISizer<string> {
	public int GetSizeInBytes(string t) => MemSizer.SizeOf(t);
}
