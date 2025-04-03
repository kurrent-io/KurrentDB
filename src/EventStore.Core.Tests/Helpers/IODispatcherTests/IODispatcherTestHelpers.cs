// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.LogRecords;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Core.Tests.Helpers.IODispatcherTests;

public static class IODispatcherTestHelpers {
	public static ResolvedEvent[] CreateResolvedEvent<TLogFormat, TStreamId>(string stream, string eventType, string data,
		string metadata = "", long eventNumber = 0) {
		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamIdIgnored = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeIdIgnored = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var record = new EventRecord(eventNumber, LogRecord.Prepare(recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			streamIdIgnored, eventNumber, PrepareFlags.None, eventTypeIdIgnored, Encoding.UTF8.GetBytes(data),
			Encoding.UTF8.GetBytes(metadata)), stream, eventType);
		return new ResolvedEvent[] {
			ResolvedEvent.ForUnresolvedEvent(record, 0)
		};
	}

	public static void SubscribeIODispatcher(IODispatcher ioDispatcher, ISubscriber bus) {
		bus.Subscribe<IODispatcherDelayedMessage>(ioDispatcher);
		bus.Subscribe<ClientMessage.NotHandled>(ioDispatcher);
		bus.Subscribe(ioDispatcher.ForwardReader);
		bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(ioDispatcher.BackwardReader);
		bus.Subscribe<ClientMessage.NotHandled>(ioDispatcher.BackwardReader);
		bus.Subscribe(ioDispatcher.Writer);
		bus.Subscribe(ioDispatcher.Awaker);
		bus.Subscribe(ioDispatcher.StreamDeleter);
	}
}
