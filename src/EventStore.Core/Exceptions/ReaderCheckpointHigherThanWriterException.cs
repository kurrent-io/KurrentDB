// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Exceptions;

public class ReaderCheckpointHigherThanWriterException : Exception {
	public ReaderCheckpointHigherThanWriterException(string checkpointName)
		: base(string.Format("Checkpoint '{0}' has greater value than writer checkpoint.", checkpointName)) {
	}
}
