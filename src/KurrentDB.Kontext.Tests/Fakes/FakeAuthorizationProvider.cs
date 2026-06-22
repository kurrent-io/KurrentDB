// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using EventStore.Plugins;
using EventStore.Plugins.Authorization;
using Microsoft.AspNetCore.Http;

namespace KurrentDB.Kontext.Tests.Fakes;

public sealed class FakeAuthorizationProvider : Plugin, IAuthorizationProvider {
	readonly HashSet<string> _denied = [];

	public FakeAuthorizationProvider Deny(string stream) {
		_denied.Add(stream);
		return this;
	}

	public ValueTask<bool> CheckAccessAsync(ClaimsPrincipal cp, Operation operation, CancellationToken ct) {
		var streamId = operation.Parameters.ToArray()
			.FirstOrDefault(p => p.Name == "streamId").Value;
		return ValueTask.FromResult(streamId == null || !_denied.Contains(streamId));
	}
}

public sealed class StaticHttpContextAccessor : IHttpContextAccessor {
	public HttpContext? HttpContext { get; set; } = new DefaultHttpContext {
		User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "test"))
	};
}