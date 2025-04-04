// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace EventStore.Core.Data;

public enum FilteredReadAllResult {
	Success = 0,
	NotModified = 1,
	Error = 2,
	AccessDenied = 3,
	Expired = 4,
	InvalidPosition = 5,
}
