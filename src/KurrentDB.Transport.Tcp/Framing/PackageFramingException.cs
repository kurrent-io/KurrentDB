// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Transport.Tcp.Framing;

public class PackageFramingException : Exception {
	public PackageFramingException(string message) : base(message) {
	}
}
