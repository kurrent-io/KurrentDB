// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Transport.Http.Atom;

public class AtomSpecificationViolationException : Exception {
	public AtomSpecificationViolationException(string message) : base(message) {
	}
}

public class ThrowHelper {
	public static void ThrowSpecificationViolation(string message) {
		throw new AtomSpecificationViolationException(message);
	}
}
