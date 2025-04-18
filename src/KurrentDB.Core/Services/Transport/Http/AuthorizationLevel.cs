// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Services.Transport.Http;

public enum AuthorizationLevel {
	None,
	User,
	Ops,
	Admin
}
