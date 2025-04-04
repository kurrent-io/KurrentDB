// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Authentication.InternalAuthentication;

public abstract class PasswordHashAlgorithm {
	public abstract void Hash(string password, out string hash, out string salt);
	public abstract bool Verify(string password, string hash, string salt);
}
