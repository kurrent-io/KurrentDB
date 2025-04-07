// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Services.Processing.TransactionFile;

public partial class HeadingEventReader {
	private abstract class Item {
		public readonly TFPos Position;

		protected Item(TFPos position) {
			Position = position;
		}

		public abstract void Handle(IReaderSubscription subscription);
	}
}
