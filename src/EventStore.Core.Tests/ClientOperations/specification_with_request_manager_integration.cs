// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Services.Replication;
using NUnit.Framework;

namespace EventStore.Core.Tests.ClientOperations;

public abstract class specification_with_request_manager_integration<TLogFormat, TStreamId> : specification_with_bare_vnode<TLogFormat, TStreamId> {

	protected long CompletionMessageCount;
	protected StorageMessage.RequestCompleted CompletionMessage;

	protected Guid InternalCorrId = Guid.NewGuid();
	protected Guid ClientCorrId = Guid.NewGuid();
	protected FakeEnvelope Envelope;

	protected abstract IEnumerable<Message> WithInitialMessages();
	protected abstract Message When();

	[SetUp]
	public void Setup() {
		CreateTestNode();
		Envelope = new FakeEnvelope();

		foreach (var m in WithInitialMessages()) {
			Publish(m);
		}
		Subscribe(new AdHocHandler<StorageMessage.RequestCompleted>(msg => {
			Interlocked.Exchange(ref CompletionMessage, msg);
			Interlocked.Increment(ref CompletionMessageCount);
		}));
		Publish(When());
	}

}
