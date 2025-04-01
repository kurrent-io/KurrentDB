// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Core.Tests.ClientAPI.Helpers;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Tests.Transforms.BitFlip;
using EventStore.Core.Tests.Transforms.ByteDup;
using EventStore.Core.Tests.Transforms.WithHeader;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.Transforms.Identity;
using EventStore.Plugins.Transforms;
using NUnit.Framework;

namespace EventStore.Core.Tests.Transforms;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class TransformTests<TLogFormat, TStreamId>: SpecificationWithDirectoryPerTestFixture {
	private const int NumEvents = 1000;
	private const int BatchSize = 50;

	[TestCase("identity", false)]
	[TestCase("identity", true)]
	[TestCase("bitflip", false)]
	[TestCase("bitflip", true)]
	[TestCase("bytedup", false)]
	[TestCase("bytedup", true)]
	[TestCase("withheader", false)]
	[TestCase("withheader", true)]
	public async Task transform_works(string transform, bool memDb) {
		MiniNode<TLogFormat, TStreamId> node = null;
		IEventStoreConnection connection = null;
		var dbPath = Path.Combine(PathName, $"node-{Guid.NewGuid()}");
		try {
			(node, connection) = await CreateNode(dbPath, transform, memDb);

			var writtenIds = await WriteEvents(connection);
			await VerifyEvents(connection, writtenIds);

			if (memDb)
				return;

			// if it's an on-disk database, close and re-open the database,
			await ShutdownNode(node, connection, keepDb: true);
			(node, connection) = await CreateNode(dbPath, transform, memDb: false);

			// then verify the chunk checksums
			await VerifyChecksums(node);

			// and verify the events again
			await VerifyEvents(connection, writtenIds);
		} finally {
			await ShutdownNode(node, connection);
		}
	}

	private async ValueTask VerifyChecksums(MiniNode<TLogFormat,TStreamId> node, CancellationToken token = default) {
		var completedChunks = new List<TFChunk>();
		for (var i = 0 ; ; i++) {
			try {
				var chunk = await node.Db.Manager.GetInitializedChunk(i, CancellationToken.None);
				if (chunk.IsReadOnly)
					completedChunks.Add(chunk);
			} catch (ArgumentOutOfRangeException) {
				break;
			}
		}

		foreach(var chunk in completedChunks)
			await chunk.VerifyFileHash(token);
	}

	private async Task<(MiniNode<TLogFormat,TStreamId>, IEventStoreConnection)> CreateNode(string dbPath, string transform, bool memDb) {
		IDbTransform dbTransform = transform switch {
			"identity" => new IdentityDbTransform(),
			"bitflip" => new BitFlipDbTransform(),
			"bytedup" => new ByteDupDbTransform(),
			"withheader" => new WithHeaderDbTransform(),
			_ => throw new ArgumentOutOfRangeException()
		};

		var node = new MiniNode<TLogFormat, TStreamId>(
			pathname: PathName,
			dbPath: dbPath,
			inMemDb: memDb,
			chunkSize: 10_000,
			cachedChunkSize: (10_000 + ChunkHeader.Size + ChunkFooter.Size) * 2,
			transform: dbTransform.Name,
			newTransforms: [dbTransform]);
		await node.Start();

		var connection = BuildConnection(node);
		await connection.ConnectAsync();

		return (node, connection);
	}

	private static async Task ShutdownNode(
		MiniNode<TLogFormat, TStreamId> node,
		IEventStoreConnection connection,
		bool keepDb = false) {
		if (node is not null)
			await node.Shutdown(keepDb);

		connection?.Dispose();
	}

	private static IEventStoreConnection BuildConnection(MiniNode<TLogFormat, TStreamId> node) {
		return TestConnection.Create(node.TcpEndPoint);
	}

	private static async Task<Guid[]> WriteEvents(IEventStoreConnection connection) {
		var writtenIds = new List<Guid>();

		for (var i = 0; i < NumEvents / BatchSize; i++) {
			var events = CreateEventBatch(BatchSize);
			await connection.AppendToStreamAsync("test", ExpectedVersion.Any, events);
			writtenIds.AddRange(events.Select(x => x.EventId));
		}

		return writtenIds.ToArray();
	}

	private static EventData[] CreateEventBatch(int numEvents) {
		var events = new EventData[numEvents];

		for (var i = 0; i < numEvents; i++) {
			events[i] = new(eventId: Guid.NewGuid(),
				type: "testEvent",
				isJson: true,
				data: "{ \"foo\":\"bar\" }"u8.ToArray(),
				metadata: null);
		}

		return events;
	}

	private static async Task VerifyEvents(IEventStoreConnection connection, Guid[] writtenIds) {
		StreamEventsSlice slice;
		var nextEventNumber = 0L;
		var readIds = new List<Guid>();
		do {
			slice = await connection.ReadStreamEventsForwardAsync("test", nextEventNumber, BatchSize, resolveLinkTos: false);
			readIds.AddRange(slice.Events.Select(evt => evt.Event.EventId));
			nextEventNumber = slice.NextEventNumber;
		} while (!slice.IsEndOfStream);

		Assert.AreEqual(NumEvents, readIds.Count);
		Assert.True(writtenIds.SequenceEqual(readIds));
	}
}
