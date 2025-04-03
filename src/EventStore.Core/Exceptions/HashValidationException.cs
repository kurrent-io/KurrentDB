// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Exceptions;

public class HashValidationException : Exception {
	public HashValidationException() {
	}

	public HashValidationException(string message) : base(message) {
	}

	public HashValidationException(string message, Exception innerException) : base(message, innerException) {
	}
}
