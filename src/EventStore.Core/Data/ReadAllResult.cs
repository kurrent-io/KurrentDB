// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public enum ReadAllResult {
	Success = 0,
	NotModified = 1,
	Error = 2,
	AccessDenied = 3,
	Expired = 4,
	InvalidPosition = 5
}
