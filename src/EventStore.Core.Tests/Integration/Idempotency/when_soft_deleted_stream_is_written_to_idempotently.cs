// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.UserManagement;
using NUnit.Framework;

namespace EventStore.Core.Tests.Integration.Idempotency;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_soft_deleted_stream_is_written_to_idempotently<TLogFormat, TStreamId> : specification_with_a_single_node<TLogFormat, TStreamId> {
	private readonly string _streamId;
	private readonly Event[] _events;

	public when_soft_deleted_stream_is_written_to_idempotently() {
		_streamId = $"{nameof(when_soft_deleted_stream_is_written_to_idempotently<TLogFormat, TStreamId>)}-{Guid.NewGuid()}";
		_events = new[] {new Event(Guid.NewGuid(), "event-type", false, new byte[] { }, new byte[] { })};
	}

	protected override async Task Given() {
		var writeEventsCompleted = new TaskCompletionSource<bool>();
		_node.Node.MainQueue.Publish(new ClientMessage.WriteEvents(Guid.NewGuid(), Guid.NewGuid(),
			new CallbackEnvelope(
				_ => {
					writeEventsCompleted.SetResult(true);
				}), false, _streamId, ExpectedVersion.NoStream, _events, SystemAccounts.System));

		await writeEventsCompleted.Task
			.WithTimeout(TimeSpan.FromSeconds(2));

		var deleteStreamCompleted = new TaskCompletionSource<bool>();
		_node.Node.MainQueue.Publish(new ClientMessage.DeleteStream(Guid.NewGuid(), Guid.NewGuid(),
			new CallbackEnvelope(
				_ => {
					deleteStreamCompleted.SetResult(true);
				}), false, _streamId, ExpectedVersion.Any, false, SystemAccounts.System));

		await deleteStreamCompleted.Task
			.WithTimeout(TimeSpan.FromSeconds(2));
	}

	[Test]
	public async Task should_return_negative_1_as_log_position() {
		var writeEventsCompleted = new TaskCompletionSource<ClientMessage.WriteEventsCompleted>();
		
		_node.Node.MainQueue.Publish(new ClientMessage.WriteEvents(Guid.NewGuid(), Guid.NewGuid(),
			new CallbackEnvelope(
				msg => {
					writeEventsCompleted.SetResult(msg as ClientMessage.WriteEventsCompleted);
				}), false, _streamId, ExpectedVersion.NoStream, _events, SystemAccounts.System));
		
		var completed = await writeEventsCompleted.Task
			.WithTimeout(TimeSpan.FromSeconds(2));
		
		Assert.AreEqual(-1, completed.CommitPosition);
		Assert.AreEqual(-1, completed.PreparePosition);
	}
}
