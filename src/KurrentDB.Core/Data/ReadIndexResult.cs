// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Data;

public enum ReadIndexResult {
	Success = 0,
	NotModified = 1,
	Error = 2,
	IndexNotFound = 3,
	Expired = 4,
	InvalidPosition = 5,
	AccessDenied = 6,
}
