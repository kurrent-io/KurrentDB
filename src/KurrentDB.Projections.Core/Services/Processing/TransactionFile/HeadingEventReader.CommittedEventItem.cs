// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;

namespace KurrentDB.Projections.Core.Services.Processing.TransactionFile;

public partial class HeadingEventReader {
	private class CommittedEventItem : Item {
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
