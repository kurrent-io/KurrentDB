// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing;

public static class SecondaryIndexingPluginFactory {
	// TODO: For now, it's a dummy method, but it'll eventually get needed classes like IPublisher, ISubscriber and setup plugin
	public static ISecondaryIndexingPlugin Create() =>
		new SecondaryIndexingPlugin();
}
