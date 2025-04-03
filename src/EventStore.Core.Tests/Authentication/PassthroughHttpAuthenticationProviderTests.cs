// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Authentication;
using EventStore.Core.Authentication.DelegatedAuthentication;
using EventStore.Core.Authentication.PassthroughAuthentication;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Plugins.Authentication;
using NUnit.Framework;

namespace EventStore.Core.Tests.Authentication;

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
