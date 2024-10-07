// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Messaging;

namespace EventStore.Core.Messages;

public static partial class ClusterClientMessage {
	[DerivedMessage(CoreMessage.ClusterClient)]
	public partial class CleanCache : Message {
		public CleanCache() { }
	}
}
