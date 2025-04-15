// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Plugins.Authentication;
using KurrentDB.Core.Authentication.DelegatedAuthentication;
using KurrentDB.Core.Authentication.PassthroughAuthentication;
using KurrentDB.Core.Services.Transport.Http.Authentication;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Authentication;

[TestFixture]
public class PassthroughHttpAuthenticationProviderTests {
	[Test]
	public void WrongProviderThrows() =>
		Assert.Throws<ArgumentException>(() => new PassthroughHttpAuthenticationProvider(new TestAuthenticationProvider()));

	[TestCaseSource(nameof(TestCases))]
	public void CorrectProviderDoesNotThrow(IAuthenticationProvider provider) =>
		Assert.DoesNotThrow(() => new PassthroughHttpAuthenticationProvider(provider));

	public static IEnumerable<object[]> TestCases() {
		yield return [new DelegatedAuthenticationProvider(new PassthroughAuthenticationProvider())];
		yield return [new PassthroughAuthenticationProvider()];
	}

	class TestAuthenticationProvider : AuthenticationProviderBase {
		public override void Authenticate(AuthenticationRequest authenticationRequest) =>
			throw new NotImplementedException();
	}
}
