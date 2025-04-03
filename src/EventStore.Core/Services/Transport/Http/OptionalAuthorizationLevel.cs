// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.Transport.Http;

internal class OptionalAuthorizationLevel(AuthorizationLevel authorizationLevel) {
	private readonly AuthorizationLevel _authorizationLevel = authorizationLevel;

	public static implicit operator AuthorizationLevel(OptionalAuthorizationLevel level) => level._authorizationLevel;
}
