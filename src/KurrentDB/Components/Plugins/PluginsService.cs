// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using EventStore.Plugins;
using KurrentDB.Core;

namespace KurrentDB.Components.Plugins;

public class PluginsService(ClusterVNodeOptions options) {
	public readonly IReadOnlyList<IPlugableComponent> PluggableComponents = options.PlugableComponents.OrderBy(x => x.Name).ToList();

	// Feature gate used by other pages (e.g. Query, Stats) to decide whether to render a feature or a
	// "not enabled" notice. The subsystem inventory shown on the Plugins page is gated in the UI by
	// [Authorize(Policy = UiPolicies.ViewSubsystems)] (Operations.Node.Information.Subsystems), so no
	// authorized read method is needed here.
	public bool IsPluginEnabled(string name) => PluggableComponents.Any(x => x.Name == name && x.Enabled);
}
