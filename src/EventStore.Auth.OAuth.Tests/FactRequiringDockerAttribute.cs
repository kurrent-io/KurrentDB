// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Xunit;

namespace EventStore.Auth.OAuth.Tests;

public sealed class FactRequiringDockerAttribute : FactAttribute {
	public FactRequiringDockerAttribute() {
		if (OperatingSystem.IsWindows()) {
			var gha = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") ?? "false";
			if (gha.Equals("true", StringComparison.InvariantCultureIgnoreCase)) {
				Skip = "Windows machines in GitHub actions do not have access to linux docker containers.";
			}
		}
	}
}
