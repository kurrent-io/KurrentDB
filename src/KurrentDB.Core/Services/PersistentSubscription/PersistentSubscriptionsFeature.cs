// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.CodeAnalysis;
using DotNext;
using EventStore.Plugins;

namespace KurrentDB.Core.Services.PersistentSubscription;

/// <summary>
/// Whether this build runs persistent subscriptions. When the switch is off, the node starts no persistent
/// subscription service, queue or endpoints, and a trimmed application drops the code that wires them.
/// </summary>
public static class PersistentSubscriptionsFeature {
	public const string FeatureName = $"{IPlugableComponent.FeatureNamePrefix}.PersistentSubscriptions";

	[FeatureSwitchDefinition(FeatureName)]
	public static bool IsAllowed { get; } = AppContext.IsFeatureSupported(FeatureName);
}
