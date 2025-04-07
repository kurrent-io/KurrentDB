// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Services.Processing.TransactionFile;

public partial class HeadingEventReader {
	private class PartitionDeletedItem : Item {
		public readonly ReaderSubscriptionMessage.EventReaderPartitionDeleted Message;

		public PartitionDeletedItem(ReaderSubscriptionMessage.EventReaderPartitionDeleted message)
			: base(message.DeleteLinkOrEventPosition.Value) {
			Message = message;
		}

		public override void Handle(IReaderSubscription subscription) {
			subscription.Handle(Message);
		}
	}
}
