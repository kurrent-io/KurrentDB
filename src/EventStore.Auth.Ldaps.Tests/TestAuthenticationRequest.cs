// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Security.Claims;
using EventStore.Plugins.Authentication;

namespace EventStore.Auth.Ldaps.Tests;

public class TestAuthenticationRequest : AuthenticationRequest {
	private Action<ClaimsPrincipal> _onAuthenticated;
	private Action _onUnauthorized;
	private Action _onError;
	private Action _onNotReady;

	public TestAuthenticationRequest(IReadOnlyDictionary<string, string> tokens) : base("test", tokens) {
	}

	public TestAuthenticationRequest(
		string name,
		string suppliedPassword,
		Action<ClaimsPrincipal> onAuthenticated,
		Action onUnauthorized = null,
		Action onError = null,
		Action onNotReady = null) : base("test", new Dictionary<string, string> {
			["uid"] = name,
			["pwd"] = suppliedPassword
		}) {
		_onAuthenticated = onAuthenticated;
		_onUnauthorized = onUnauthorized;
		_onError = onError;
		_onNotReady = onNotReady;
	}

	public override void Unauthorized() {
		_onUnauthorized?.Invoke();
	}

	public override void Authenticated(ClaimsPrincipal principal) {
		_onAuthenticated?.Invoke(principal);
	}

	public override void Error() {
		_onError?.Invoke();
	}

	public override void NotReady() {
		_onNotReady?.Invoke();
	}
}

public class TestAuthenticationResponse {
	public readonly ClaimsPrincipal Principal;
	public readonly string FailureReason;

	public TestAuthenticationResponse(ClaimsPrincipal principal) {
		Principal = principal;
	}

	public TestAuthenticationResponse(string failureReason) {
		FailureReason = failureReason;
	}
}
