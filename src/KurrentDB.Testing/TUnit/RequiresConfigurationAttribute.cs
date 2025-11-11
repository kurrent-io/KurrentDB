// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).


// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Testing;

namespace KurrentDB.Testing.TUnit;

public class RequiresConfigurationAttribute(string key) : SkipAttribute($"Requires configuration key: \"{key}\"") {
	public override Task<bool> ShouldSkip(TestRegisteredContext context) {
		var value = ToolkitTestEnvironment.Configuration[key];
		return Task.FromResult(string.IsNullOrWhiteSpace(value));
	}
}
