// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Services.Processing.TransactionFile;

public partial class HeadingEventReader
{
	private class CommittedEventItem : HeadingEventReader.Item {
		public readonly ReaderSubscriptionMessage.CommittedEventDistributed Message;

		public CommittedEventItem(ReaderSubscriptionMessage.CommittedEventDistributed message)
			: base(message.Data.Position) {
			Message = message;
		}

		public override void Handle(IReaderSubscription subscription) {
			subscription.Handle(Message);
		}

		public override string ToString() {
			return string.Format(
				"{0} : {2}@{1}",
				Message.Data.EventType,
				Message.Data.PositionStreamId,
				Message.Data.PositionSequenceNumber);
		}
	}
}
