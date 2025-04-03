// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Security.Claims;
using EventStore.Core.Messages;
using NUnit.Framework;

namespace EventStore.Core.Tests.Authentication;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_multiple_requests_with_the_same_correct_user_name_and_password<TLogFormat, TStreamId> :
	with_internal_authentication_provider<TLogFormat, TStreamId> {
	private bool _unauthorized;
	private ClaimsPrincipal _authenticatedAs;
	private bool _error;

	protected override void Given() {
		base.Given();
		ExistingEvent("$user-user", "$user", null, "{LoginName:'user', Salt:'drowssap',Hash:'password'}");
	}

	[SetUp]
	public void SetUp() {
		SetUpProvider();

		_internalAuthenticationProvider.Authenticate(
			new TestAuthenticationRequest("user", "password", () => { }, p => { }, () => { }, () => { }));

		_consumer.HandledMessages.Clear();

		_internalAuthenticationProvider.Authenticate(
			new TestAuthenticationRequest(
				"user", "password", () => _unauthorized = true, p => _authenticatedAs = p, () => _error = true,
				() => { }));
	}

	[Test]
	public void authenticates_user() {
		Assert.IsFalse(_unauthorized);
		Assert.IsFalse(_error);
		Assert.NotNull(_authenticatedAs);
		Assert.IsTrue(_authenticatedAs.Identity.Name == "user");
	}

	[Test]
	public void does_not_publish_any_read_requests() {
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsBackward>().Count());
		Assert.AreEqual(0, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Count());
	}
}
