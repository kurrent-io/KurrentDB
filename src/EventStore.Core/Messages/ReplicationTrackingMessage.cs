// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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

		public IndexedTo(long logPosition) {
			Ensure.Nonnegative(logPosition + 1, "logPosition");
			LogPosition = logPosition;
		}
	}

	[DerivedMessage(CoreMessage.ReplicationTracking)]
	public partial class ReplicatedTo : Message {
		public readonly long LogPosition;

		public ReplicatedTo(long logPosition) {
			Ensure.Nonnegative(logPosition + 1, "logPosition");
			LogPosition = logPosition;
		}
	}

	[DerivedMessage(CoreMessage.ReplicationTracking)]
	public partial class LeaderReplicatedTo : Message {
		public readonly long LogPosition;

		public LeaderReplicatedTo(long logPosition) {
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
