// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Cluster;
using KurrentDB.Core.Messaging;
using KurrentDB.Common.Utils;

namespace EventStore.Core.Messages;

public static partial class LeaderDiscoveryMessage {
	[DerivedMessage(CoreMessage.LeaderDiscovery)]
	public partial class LeaderFound : Message {
		public readonly MemberInfo Leader;

		public LeaderFound(MemberInfo leader) {
			Ensure.NotNull(leader, "leader");
			Leader = leader;
		}
	}

	[DerivedMessage(CoreMessage.LeaderDiscovery)]
	public partial class DiscoveryTimeout : Message {
	}
}
