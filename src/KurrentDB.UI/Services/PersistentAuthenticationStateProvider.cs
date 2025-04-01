// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace KurrentDB.UI.Services;

class PersistentAuthenticationStateProvider : AuthenticationStateProvider {
	static readonly Task<AuthenticationState> DefaultUnauthenticatedTask = Task.FromResult(new AuthenticationState(new(new ClaimsIdentity())));

	readonly Task<AuthenticationState> _authenticationStateTask = DefaultUnauthenticatedTask;

	public PersistentAuthenticationStateProvider(PersistentComponentState state) {
		if (!state.TryTakeFromJson<UserInfo>(nameof(UserInfo), out var userInfo) || userInfo is null) {
			return;
		}

		Claim[] claims = [new(ClaimTypes.NameIdentifier, userInfo.UserId), new(ClaimTypes.Name, userInfo.UserId)];

		_authenticationStateTask = Task.FromResult(new AuthenticationState(new(new ClaimsIdentity(claims, authenticationType: nameof(PersistentAuthenticationStateProvider)))));
	}

	public override Task<AuthenticationState> GetAuthenticationStateAsync() => _authenticationStateTask;
}
