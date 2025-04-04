// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Cryptography;

namespace EventStore.Licensing.Tests;

public static class Keys {
	static Keys() {
		using var rsa = RSA.Create(512);
		Public = Convert.ToBase64String(rsa.ExportRSAPublicKey());
		Private = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
	}

	public static string Public { get; }
	public static string Private { get; }
}
