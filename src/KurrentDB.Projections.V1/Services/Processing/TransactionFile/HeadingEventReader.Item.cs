// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;

namespace KurrentDB.Projections.Core.Services.Processing.TransactionFile;

public partial class HeadingEventReader {
	private abstract class Item {
		public readonly TFPos Position;

		protected Item(TFPos position) {
			Position = position;
		}

		public abstract void Handle(IReaderSubscription subscription);
	}
}
