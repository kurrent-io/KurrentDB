// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Exceptions;

public class BadChunkInDatabaseException : Exception {
	public BadChunkInDatabaseException(string message) : base(message) {
	}
}
