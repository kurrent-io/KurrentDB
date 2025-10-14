// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

class InvalidEmittedEventSequenceException : Exception {
	public InvalidEmittedEventSequenceException(string message)
		: base(message) {
	}
}
