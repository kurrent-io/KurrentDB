// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Data;

namespace EventStore.Core.Tests.Replication.ReadStream;

public static class ReplicationTestHelper {
	private static TimeSpan _timeout = TimeSpan.FromSeconds(20);

	public static ClientMessage.WriteEventsCompleted WriteEvent<TLogFormat, TStreamId>(
		MiniClusterNode<TLogFormat, TStreamId> node, Event[] events,
		string streamId) {
		var resetEvent = new ManualResetEventSlim();
		ClientMessage.WriteEventsCompleted writeResult = null;
		node.Node.MainQueue.Publish(new ClientMessage.WriteEvents(Guid.NewGuid(), Guid.NewGuid(),
			new CallbackEnvelope(msg => {
				writeResult = (ClientMessage.WriteEventsCompleted)msg;
				resetEvent.Set();
			}), false, streamId, -1, events,
			SystemAccounts.System, new Dictionary<string, string> {
				["uid"] = SystemUsers.Admin,
				["pwd"] = SystemUsers.DefaultAdminPassword
			}));
		if (!resetEvent.Wait(_timeout)) {
			Assert.Fail("Timed out waiting for event to be written");
			return null;
		}

		return writeResult;
	}

	public static ClientMessage.ReadAllEventsForwardCompleted ReadAllEventsForward<TLogFormat, TStreamId>(
		MiniClusterNode<TLogFormat, TStreamId> node,
		long position) {
		ClientMessage.ReadAllEventsForwardCompleted readResult = null;
		var readEvent = new ManualResetEventSlim();
		var done = false;
		while (!done) {
			var read = new ClientMessage.ReadAllEventsForward(Guid.NewGuid(), Guid.NewGuid(), new CallbackEnvelope(
					msg => {
						readResult = (ClientMessage.ReadAllEventsForwardCompleted)msg;
						readEvent.Set();
					}),
				0, 0, 100, false, false, null, SystemAccounts.System, replyOnExpired: false);
			node.Node.MainQueue.Publish(read);

			if (!readEvent.Wait(_timeout)) {
				Assert.Fail("Timed out waiting for events to be read forward");
				return null;
			}

			if (readResult.Result == ReadAllResult.Error) {
				Assert.Fail("Failed to read forwards. Read result error: {0}", readResult.Error);
				return null;
			}

			done = readResult.NextPos.CommitPosition > position;
			readEvent.Reset();
		}

		return readResult;
	}

	public static ClientMessage.ReadAllEventsBackwardCompleted ReadAllEventsBackward<TLogFormat, TStreamId>(
		MiniClusterNode<TLogFormat, TStreamId> node,
		long position) {
		ClientMessage.ReadAllEventsBackwardCompleted readResult = null;
		var resetEvent = new ManualResetEventSlim();
		var done = false;
		while (!done) {
			resetEvent.Reset();
			var read = new ClientMessage.ReadAllEventsBackward(Guid.NewGuid(), Guid.NewGuid(), new CallbackEnvelope(
					msg => {
						readResult = (ClientMessage.ReadAllEventsBackwardCompleted)msg;
						resetEvent.Set();
					}),
				-1, -1, 100, false, false, null, SystemAccounts.System);
			node.Node.MainQueue.Publish(read);

			if (!resetEvent.Wait(_timeout)) {
				Assert.Fail("Timed out waiting for events to be read backward");
				return null;
			}

			if (readResult.Result == ReadAllResult.Error) {
				Assert.Fail("Failed to read backwards. Read result error: {0}", readResult.Error);
				return null;
			}

			done = readResult.NextPos.CommitPosition < position;
		}

		return readResult;
	}

	public static ClientMessage.ReadStreamEventsForwardCompleted ReadStreamEventsForward<TLogFormat, TStreamId>(
		MiniClusterNode<TLogFormat, TStreamId> node,
		string streamId) {
		ClientMessage.ReadStreamEventsForwardCompleted readResult = null;
		var resetEvent = new ManualResetEventSlim();
		var read = new ClientMessage.ReadStreamEventsForward(Guid.NewGuid(), Guid.NewGuid(), new CallbackEnvelope(
				msg => {
					readResult = (ClientMessage.ReadStreamEventsForwardCompleted)msg;
					resetEvent.Set();
				}), streamId, 0, 10,
			false, false, null, SystemAccounts.System, replyOnExpired: false);
		node.Node.MainQueue.Publish(read);

		if (!resetEvent.Wait(_timeout)) {
			Assert.Fail("Timed out waiting for the stream to be read forward");
			return null;
		}

		return readResult;
	}

	public static ClientMessage.ReadStreamEventsBackwardCompleted ReadStreamEventsBackward<TLogFormat, TStreamId>(
		MiniClusterNode<TLogFormat, TStreamId> node,
		string streamId) {
		ClientMessage.ReadStreamEventsBackwardCompleted readResult = null;
		var resetEvent = new ManualResetEventSlim();
		var read = new ClientMessage.ReadStreamEventsBackward(Guid.NewGuid(), Guid.NewGuid(), new CallbackEnvelope(
				msg => {
					readResult = (ClientMessage.ReadStreamEventsBackwardCompleted)msg;
					resetEvent.Set();
				}), streamId, 9, 10,
			false, false, null, SystemAccounts.System);
		node.Node.MainQueue.Publish(read);

		if (!resetEvent.Wait(_timeout)) {
			Assert.Fail("Timed out waiting for the stream to be read backward");
			return null;
		}

		return readResult;
	}

	public static ClientMessage.ReadEventCompleted ReadEvent<TLogFormat, TStreamId>(
		MiniClusterNode<TLogFormat, TStreamId> node, string streamId,
		long eventNumber) {
		ClientMessage.ReadEventCompleted readResult = null;
		var resetEvent = new ManualResetEventSlim();
		var read = new ClientMessage.ReadEvent(Guid.NewGuid(), Guid.NewGuid(), new CallbackEnvelope(msg => {
				readResult = (ClientMessage.ReadEventCompleted)msg;
				resetEvent.Set();
			}), streamId, eventNumber,
			false, false, SystemAccounts.System);
		node.Node.MainQueue.Publish(read);

		if (!resetEvent.Wait(_timeout)) {
			Assert.Fail("Timed out waiting for the event to be read");
			return null;
		}

		return readResult;
	}
}
