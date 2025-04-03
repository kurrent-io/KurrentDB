// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Projections.Core.Services.Processing.Subscriptions;

namespace EventStore.Projections.Core.Services.Processing.TransactionFile;

public partial class HeadingEventReader
{
	private abstract class Item {
		public readonly TFPos Position;

		protected Item(TFPos position) {
			Position = position;
		}

		public abstract void Handle(IReaderSubscription subscription);
	}
}
