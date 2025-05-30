// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Common.Exceptions;

public class ApplicationInitializationException : Exception {
	public ApplicationInitializationException(string message) : base(message) {
	}

	public ApplicationInitializationException(string message, Exception innerException) : base(message,
		innerException) {
	}
}
