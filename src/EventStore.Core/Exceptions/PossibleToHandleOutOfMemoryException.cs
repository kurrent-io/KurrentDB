// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Exceptions;

internal class PossibleToHandleOutOfMemoryException : OutOfMemoryException {
	public PossibleToHandleOutOfMemoryException() {
	}

	public PossibleToHandleOutOfMemoryException(string message) : base(message) {
	}

	public PossibleToHandleOutOfMemoryException(string message, Exception innerException) : base(message,
		innerException) {
	}
}
