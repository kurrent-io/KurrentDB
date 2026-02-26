// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Plugins;
using KurrentDB.Core;

namespace KurrentDB.Components.Plugins;

public class PluginsService(ClusterVNodeOptions options) {
	public readonly IReadOnlyList<IPlugableComponent> PluggableComponents = options.PlugableComponents.OrderBy(x => x.Name).ToList();

	public bool IsPluginEnabled(string name) => options.IsPluginEnabled(name);
}
