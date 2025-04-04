// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public enum ReadEventResult {
	Success = 0,
	NotFound = 1,
	NoStream = 2,
	StreamDeleted = 3,
	Error = 4,
	AccessDenied = 5
}
