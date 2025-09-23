// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.EventReaders.Feeds;
using KurrentDB.Projections.Core.Tests.Services.event_reader;

namespace KurrentDB.Projections.Core.Tests.Services.feed_reader;

public abstract class TestFixtureWithFeedReaderService<TLogFormat, TStreamId> : TestFixtureWithEventReaderService<TLogFormat, TStreamId> {
	protected FeedReaderService _feedReaderService;

	protected override void Given1() {
		base.Given1();
		EnableReadAll();
	}

	protected override void GivenAdditionalServices() {
		_feedReaderService = new FeedReaderService(SubscriptionDispatcher, _timeProvider);
		_bus.Subscribe(_feedReaderService);
	}
}
