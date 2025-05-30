﻿// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled;

public class PolicyInformation {
	public readonly DateTimeOffset Expires;
	public readonly string Name;

	public PolicyInformation(string name, long version, DateTimeOffset expires) {
		Version = version;
		Name = name;
		Expires = expires;
	}

	public long Version { get; }

	public override string ToString() {
		return $"Policy : {Name} {Version} {Expires}";
	}
}
