// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Common.Options;

public enum LogLevel {
	Default = 0,
	Verbose = 1,
	Debug = 2,
	Information = 3,
	Warning = 4,
	Error = 5,
	Fatal = 6,

	// support for MS levels
	Trace = Verbose,
	Critical = Fatal,
	None = Fatal,
}
