// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Exceptions;

internal class UnableToReadPastEndOfStreamException : Exception {
	public UnableToReadPastEndOfStreamException() {
	}

	public UnableToReadPastEndOfStreamException(string message) : base(message) {
	}

	public UnableToReadPastEndOfStreamException(string message, Exception innerException) : base(message,
		innerException) {
	}
}
