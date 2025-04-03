// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public enum ReadStreamResult {
	Success = 0,
	NoStream = 1,
	StreamDeleted = 2,
	NotModified = 3,
	Error = 4,
	AccessDenied = 5,
	Expired = 6,
}
