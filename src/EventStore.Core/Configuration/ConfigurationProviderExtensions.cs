// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Configuration.Sources;
using Microsoft.Extensions.Configuration;

namespace EventStore.Core.Configuration;

public static class ConfigurationProviderExtensions {
	public static IEnumerable<string> GetChildKeys(this IConfigurationProvider provider) => 
		provider.GetChildKeys([], KurrentConfigurationKeys.Prefix);
}
