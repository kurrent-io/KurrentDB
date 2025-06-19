// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Event Store License v2 (see LICENSE.md).

using KurrentDB.Core.Services;

namespace KurrentDB.SecondaryIndexing.Indexes.EventType;

internal class EventTypeIndex {
	public const string IndexPrefix = $"{SystemStreams.IndexStreamPrefix}et-";
}
