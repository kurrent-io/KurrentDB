// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;

namespace EventStore.Core.Messages;

public static partial class ReplicationTrackingMessage {
	[DerivedMessage(CoreMessage.ReplicationTracking)]
	public partial class WriterCheckpointFlushed : Message {
	}

	[DerivedMessage(CoreMessage.ReplicationTracking)]
	public partial class IndexedTo : Message {
		public readonly long LogPosition;
		
		public IndexedTo(long logPosition ) {
			Ensure.Nonnegative(logPosition + 1, "logPosition");
			LogPosition = logPosition;
		}
	}

	[DerivedMessage(CoreMessage.ReplicationTracking)]
	public partial class ReplicatedTo : Message {
		public readonly long LogPosition;
		
		public ReplicatedTo(long logPosition ) {
			Ensure.Nonnegative(logPosition + 1, "logPosition");
			LogPosition = logPosition;
		}
	}

	[DerivedMessage(CoreMessage.ReplicationTracking)]
	public partial class LeaderReplicatedTo : Message {
		public readonly long LogPosition;
		
		public LeaderReplicatedTo(long logPosition ) {
			Ensure.Nonnegative(logPosition + 1, "logPosition");
			LogPosition = logPosition;
		}
	}

	[DerivedMessage(CoreMessage.ReplicationTracking)]
	public partial class ReplicaWriteAck : Message {
		public readonly Guid SubscriptionId;
		public readonly long ReplicationLogPosition;

		public ReplicaWriteAck(Guid subscriptionId, long replicationLogPosition) {
			Ensure.NotEmptyGuid(subscriptionId, "subscriptionId");
			SubscriptionId = subscriptionId;
			ReplicationLogPosition = replicationLogPosition;
		}
	}
}
