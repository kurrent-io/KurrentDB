// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public static class ExpectedVersion {
	public const long Any = -2;

	public const long NoStream = -1;

	public const long Invalid = -3;
	public const long StreamExists = -4;
}
