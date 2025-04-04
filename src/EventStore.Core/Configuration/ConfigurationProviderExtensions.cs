// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Core.Configuration.Sources;
using Microsoft.Extensions.Configuration;

namespace EventStore.Core.Configuration;

public static class ConfigurationProviderExtensions {
	public static IEnumerable<string> GetChildKeys(this IConfigurationProvider provider) =>
		provider.GetChildKeys([], EventStoreConfigurationKeys.Prefix);
}
