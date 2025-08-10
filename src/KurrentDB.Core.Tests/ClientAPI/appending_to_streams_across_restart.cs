// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using KurrentDB.Core.Tests.ClientAPI.Helpers;
using KurrentDB.Core.Tests.Helpers;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.ClientAPI;

[Category("ClientAPI"), Category("LongRunning")]
[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class appending_to_streams_across_restart<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private MiniNode<TLogFormat, TStreamId> _node;

	virtual protected IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	[TearDown]
	public override async Task TearDown() {
		await _node?.Shutdown();
		await base.TearDown();
	}

	[Test]
	[Category("Network")]
	public async Task detect_existing_streams_flush() {
		void CreateNode() {
			_node = new MiniNode<TLogFormat, TStreamId>(
				pathname: PathName,
				dbPath: Path.Combine(PathName, "mini-node-db"),
				inMemDb: false,
				streamExistenceFilterSize: 10_000,
				streamExistenceFilterCheckpointIntervalMs: 100,
				streamExistenceFilterCheckpointDelayMs: 0);
		}

		CreateNode();
		await _node.Start();

		// GIVEN some streams
		var normal = "detect_existing_streams_normal";
		var meta = "detect_existing_streams_meta";
		var committed = "detect_existing_streams_committed_transaction";
		var uncommitted = "detect_existing_streams_uncommitted_transaction";

		EventData[] GenEvents() => Enumerable.Range(0, 10).Select(x => TestEvent.NewTestEvent(Guid.NewGuid())).ToArray();
		var eventsByStream = new Dictionary<string, EventData[]>(){
			{ normal, GenEvents() },
			{ committed, GenEvents() },
			{ uncommitted, GenEvents() },
		};

		var uncommittedTransId = 0L;

		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			// normal
			Assert.AreEqual(9, await store
				.Apply(x => x.AppendToStreamAsync(normal, ExpectedVersion.NoStream, eventsByStream[normal]))
				.Apply(x => x.NextExpectedVersion));

			// meta
			Assert.AreEqual(0, await store
				.Apply(x => x.SetStreamMetadataAsync("meta", ExpectedVersion.NoStream, StreamMetadata.Create(maxCount: 5)))
				.Apply(x => x.NextExpectedVersion));

			if (LogFormatHelper<TLogFormat, TStreamId>.IsV2) {
				// committed
				Assert.AreEqual(9, await new TransactionalWriter(store, committed)
					.Apply(x => x.StartTransaction(-1))
					.Apply(x => x.Write(eventsByStream[committed]))
					.Apply(x => x.Commit())
					.Apply(x => x.NextExpectedVersion));

				// uncommitted
				uncommittedTransId = await new TransactionalWriter(store, uncommitted)
					.Apply(x => x.StartTransaction(-1))
					.Apply(x => x.Write(eventsByStream[committed]))
					.Apply(x => x.TransactionId);
			}

			// append an event to another stream because the last event is always going to get reinitialised
			Assert.AreEqual(0, await store
				.Apply(x => x.AppendToStreamAsync("another-stream", ExpectedVersion.NoStream, TestEvent.NewTestEvent(Guid.NewGuid())))
				.Apply(x => x.NextExpectedVersion));
		}

		// WHEN flush and restart
		await Task.Delay(500);
		await _node.Shutdown(keepDb: true);
		CreateNode();
		await _node.Start();

		// THEN the streams all exist
		using (var store = BuildConnection(_node)) {
			await store.ConnectAsync();

			// normal
			Assert.AreEqual(10, await store
				.Apply(x => x.AppendToStreamAsync(normal, 9, TestEvent.NewTestEvent(Guid.NewGuid())))
				.Apply(x => x.NextExpectedVersion));

			Assert.AreEqual(
				eventsByStream[normal].Length + 1,
				await EventsStream.Count(store, normal));

			// meta
			Assert.AreEqual(1, await store
				.Apply(x => x.SetStreamMetadataAsync("meta", 0, StreamMetadata.Create(maxCount: 6)))
				.Apply(x => x.NextExpectedVersion));

			Assert.AreEqual(0, await EventsStream.Count(store, meta));

			if (LogFormatHelper<TLogFormat, TStreamId>.IsV2) {
				// committed
				Assert.AreEqual(10, await store
					.Apply(x => x.AppendToStreamAsync(committed, 9, TestEvent.NewTestEvent(Guid.NewGuid())))
					.Apply(x => x.NextExpectedVersion));

				Assert.AreEqual(
					eventsByStream[committed].Length + 1,
					await EventsStream.Count(store, committed));

				// uncommitted
				Assert.AreEqual(9, await new TransactionalWriter(store, uncommitted)
					.Apply(x => x.ContinueTransaction(uncommittedTransId))
					.Apply(x => x.Commit())
					.Apply(x => x.NextExpectedVersion));

				Assert.AreEqual(
					eventsByStream[uncommitted].Length,
					await EventsStream.Count(store, uncommitted));
			}
		}
	}
}

public static class Extensions {
	public async static Task<U> Apply<T, U>(this Task<T> x, Func<T, Task<U>> f) {
		var r = await x;
		return await f(r);
	}

	public static U Apply<T, U>(this T x, Func<T, U> f) {
		return f(x);
	}

	public async static Task<U> Apply<T, U>(this T x, Func<T, Task<U>> f) {
		return await f(x);
	}

	public async static Task<U> Apply<T, U>(this Task<T> x, Func<T, U> f) {
		var r = await x;
		return f(r);
	}
}
