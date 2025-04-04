// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Collections;
using Microsoft.Extensions.Configuration;

namespace EventStore.Core.Configuration.Sources;

public class KurrentDBEnvironmentVariablesSource(IDictionary? environment = null) : IConfigurationSource {
	public IConfigurationProvider Build(IConfigurationBuilder builder) =>
		new KurrentDBEnvironmentVariablesConfigurationProvider(environment);
}
