// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.Serialization;

namespace EventStore.Core.Exceptions;

public class FileBeingDeletedException : Exception {
	public FileBeingDeletedException() {
	}

	public FileBeingDeletedException(string message) : base(message) {
	}

	public FileBeingDeletedException(string message, Exception innerException) : base(message, innerException) {
	}
}
