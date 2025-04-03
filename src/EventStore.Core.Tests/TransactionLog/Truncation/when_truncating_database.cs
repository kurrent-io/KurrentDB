// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Truncation;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_truncating_database<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	[Test, Category("LongRunning")]
	public async Task everything_should_go_fine() {
		var miniNode = new MiniNode<TLogFormat, TStreamId>(PathName, inMemDb: false);
		await miniNode.Start();

		var tcpPort = miniNode.TcpEndPoint.Port;
		var httpPort = miniNode.HttpEndPoint.Port;
		const int cnt = 50;
		var countdown = new CountdownEvent(cnt);

		// --- first part of events
		WriteEvents(cnt, miniNode, countdown);
		Assert.IsTrue(countdown.Wait(TimeSpan.FromSeconds(10)), "Took too long writing first part of events.");
		countdown.Reset();

		// -- set up truncation
		var truncatePosition = miniNode.Db.Config.WriterCheckpoint.ReadNonFlushed();
		miniNode.Db.Config.TruncateCheckpoint.Write(truncatePosition);
		miniNode.Db.Config.TruncateCheckpoint.Flush();

		// --- second part of events
		WriteEvents(cnt, miniNode, countdown);
		Assert.IsTrue(countdown.Wait(TimeSpan.FromSeconds(10)), "Took too long writing second part of events.");
		countdown.Reset();

		await miniNode.Shutdown(keepDb: true);

		// --- first restart and truncation
		miniNode = new MiniNode<TLogFormat, TStreamId>(PathName, tcpPort, httpPort, inMemDb: false);
		await miniNode.Start();
		await miniNode.Shutdown(keepDb: true);

		// --- second restart after truncation
		miniNode = new MiniNode<TLogFormat, TStreamId>(PathName, tcpPort, httpPort, inMemDb: false);
		await miniNode.Start();
		Assert.AreEqual(-1, miniNode.Db.Config.TruncateCheckpoint.Read());
		Assert.That(miniNode.Db.Config.WriterCheckpoint.Read(), Is.GreaterThanOrEqualTo(truncatePosition));

		// -- third part of events
		WriteEvents(cnt, miniNode, countdown);
		Assert.IsTrue(countdown.Wait(TimeSpan.FromSeconds(10)), "Took too long writing third part of events.");
		countdown.Reset();

		await miniNode.Shutdown(keepDb: true);

		// -- third restart
		miniNode = new MiniNode<TLogFormat, TStreamId>(PathName, tcpPort, httpPort, inMemDb: false);
		Assert.AreEqual(-1, miniNode.Db.Config.TruncateCheckpoint.Read());
		await miniNode.Start();

		// -- if we get here -- then everything is ok
		await miniNode.Shutdown();
	}

	[Test, Category("LongRunning"), Category("Network")]
	public async Task with_truncate_position_in_completed_chunk_everything_should_go_fine() {
		const int chunkSize = 1024 * 1024;
		const int cachedSize = chunkSize * 3;

		var miniNode = new MiniNode<TLogFormat, TStreamId>(PathName, chunkSize: chunkSize, cachedChunkSize: cachedSize, inMemDb: false);
		await miniNode.Start();

		var httpPort = miniNode.HttpEndPoint.Port;
		const int cnt = 1;
		var countdown = new CountdownEvent(cnt);

		// --- first part of events
		WriteEvents(cnt, miniNode, countdown, MiniNode<TLogFormat, TStreamId>.ChunkSize / 5 * 3);
		Assert.IsTrue(countdown.Wait(TimeSpan.FromSeconds(10)), "Took too long writing first part of events.");
		countdown.Reset();

		// -- set up truncation
		var truncatePosition = miniNode.Db.Config.WriterCheckpoint.ReadNonFlushed();
		miniNode.Db.Config.TruncateCheckpoint.Write(truncatePosition);
		miniNode.Db.Config.TruncateCheckpoint.Flush();

		// --- second part of events
		WriteEvents(cnt, miniNode, countdown, MiniNode<TLogFormat, TStreamId>.ChunkSize / 2);
		Assert.IsTrue(countdown.Wait(TimeSpan.FromSeconds(10)), "Took too long writing second part of events.");
		countdown.Reset();

		await miniNode.Shutdown(keepDb: true);

		var tcpPort = miniNode.TcpEndPoint.Port;

		// --- first restart and truncation
		miniNode = new MiniNode<TLogFormat, TStreamId>(PathName, tcpPort, httpPort, chunkSize: chunkSize,
			cachedChunkSize: cachedSize, inMemDb: false);
		await miniNode.Start();
		await miniNode.Shutdown(keepDb: true);

		// --- second restart after truncation
		miniNode = new MiniNode<TLogFormat, TStreamId>(PathName, tcpPort, httpPort, chunkSize: chunkSize,
			cachedChunkSize: cachedSize, inMemDb: false);
		await miniNode.Start();
		Assert.AreEqual(-1, miniNode.Db.Config.TruncateCheckpoint.Read());
		Assert.That(miniNode.Db.Config.WriterCheckpoint.Read(), Is.GreaterThanOrEqualTo(truncatePosition));

		// -- third part of events
		WriteEvents(cnt, miniNode, countdown, MiniNode<TLogFormat, TStreamId>.ChunkSize / 5);
		Assert.IsTrue(countdown.Wait(TimeSpan.FromSeconds(10)), "Took too long writing third part of events.");
		countdown.Reset();

		// -- if we get here -- then everything is ok
		await miniNode.Shutdown();
	}

	private static void WriteEvents(int cnt, MiniNode<TLogFormat, TStreamId> miniNode, CountdownEvent countdown, int dataSize = 4000) {
		for (int i = 0; i < cnt; ++i) {
			miniNode.Node.MainQueue.Publish(
				new ClientMessage.WriteEvents(Guid.NewGuid(), Guid.NewGuid(),
					new CallbackEnvelope(m => {
						Assert.IsInstanceOf<ClientMessage.WriteEventsCompleted>(m);
						var msg = (ClientMessage.WriteEventsCompleted)m;
						Assert.AreEqual(OperationResult.Success, msg.Result);
						countdown.Signal();
					}),
					true,
					"test-stream",
					ExpectedVersion.Any,
					new[] {
						new Event(Guid.NewGuid(), "test-event-type", false, new byte[dataSize], null)
					},
					null));
		}
	}
}
