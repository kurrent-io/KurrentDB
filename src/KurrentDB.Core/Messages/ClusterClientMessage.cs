// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Messages;

public static partial class ClusterClientMessage {
	[DerivedMessage(CoreMessage.ClusterClient)]
	public partial class CleanCache : Message {
		public CleanCache() { }
	}
}
