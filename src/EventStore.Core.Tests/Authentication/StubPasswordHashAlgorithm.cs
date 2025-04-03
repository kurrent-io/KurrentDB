// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Authentication.InternalAuthentication;

namespace EventStore.Core.Tests.Authentication;

public class StubPasswordHashAlgorithm : PasswordHashAlgorithm {
	public override void Hash(string password, out string hash, out string salt) {
		hash = password;
		salt = ReverseString(password);
	}

	public override bool Verify(string password, string hash, string salt) {
		return password == hash && ReverseString(password) == salt;
	}

	private static string ReverseString(string s) {
		return new String(s.Reverse().ToArray());
	}
}
