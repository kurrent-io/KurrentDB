// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.emitted_streams_deleter.when_deleting;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_delete_stream_succeeds<TLogFormat, TStreamId> : with_emitted_stream_deleter<TLogFormat, TStreamId> {
	protected Action _onDeleteStreamCompleted;
	private readonly ManualResetEventSlim _mre = new ManualResetEventSlim();
	private readonly List<ClientMessage.DeleteStream> _deleteMessages = new List<ClientMessage.DeleteStream>();

	public override void When() {
		_onDeleteStreamCompleted = () => { _mre.Set(); };

		_deleter.DeleteEmittedStreams(_onDeleteStreamCompleted);
	}

	public override void Handle(ClientMessage.DeleteStream message) {
		_deleteMessages.Add(message);
		message.Envelope.ReplyWith(new ClientMessage.DeleteStreamCompleted(
			message.CorrelationId, OperationResult.Success, String.Empty));
	}

	[Test]
	public void should_have_deleted_the_tracked_emitted_stream() {
		if (!_mre.Wait(10000)) {
			Assert.Fail("Timed out waiting for event to be deleted");
		}

		Assert.AreEqual(_testStreamName, _deleteMessages[0].EventStreamId);
		Assert.AreEqual(_checkpointName, _deleteMessages[1].EventStreamId);
	}
}
