// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages.EventReaders.Feeds;
using KurrentDB.Projections.Core.Services;

namespace KurrentDB.Projections.Core.EventReaders.Feeds;

public class FeedReaderService(ReaderSubscriptionDispatcher subscriptionDispatcher, ITimeProvider timeProvider)
	: IHandle<FeedReaderMessage.ReadPage> {
	public void Handle(FeedReaderMessage.ReadPage message) {
		var reader = FeedReader.Create(subscriptionDispatcher, message, timeProvider);
		reader.Start();
	}
}
