// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).


using KurrentDB.Core.Bus;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Projections.Core.Messages.EventReaders.Feeds;
using KurrentDB.Projections.Core.Services;

namespace KurrentDB.Projections.Core.EventReaders.Feeds;

public class FeedReaderService : IHandle<FeedReaderMessage.ReadPage> {
	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;

	private readonly ITimeProvider _timeProvider;

	public FeedReaderService(ReaderSubscriptionDispatcher subscriptionDispatcher, ITimeProvider timeProvider) {
		_subscriptionDispatcher = subscriptionDispatcher;
		_timeProvider = timeProvider;
	}

	public void Handle(FeedReaderMessage.ReadPage message) {
		var reader = FeedReader.Create(_subscriptionDispatcher, message, _timeProvider);
		reader.Start();
	}
}
