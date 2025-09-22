// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage;

namespace KurrentDB.Core.Tests.Services.Transport.Grpc.StreamsTests;

public class FakeSecondaryIndexReader : ISecondaryIndexReader {
	public bool CanReadIndex(string indexName) {
		return SystemStreams.IsIndexStream(indexName);
	}

	public TFPos GetLastIndexedPosition(string indexName) {
		return TFPos.Invalid;
	}

	public ValueTask<ClientMessage.ReadIndexEventsForwardCompleted> ReadForwards(ClientMessage.ReadIndexEventsForward msg, CancellationToken token) {
		var result = new ClientMessage.ReadIndexEventsForwardCompleted(
			result: ReadIndexResult.Success,
			events: CreateEvents(msg.MaxCount),
			currentPos: TFPos.Invalid,
			tfLastCommitPosition: TFPos.Invalid.CommitPosition,
			isEndOfStream: true,
			error: null);

		return ValueTask.FromResult(result);
	}

	public ValueTask<ClientMessage.ReadIndexEventsBackwardCompleted> ReadBackwards(ClientMessage.ReadIndexEventsBackward msg, CancellationToken token) {
		var result = new ClientMessage.ReadIndexEventsBackwardCompleted(
			result: ReadIndexResult.Success,
			events: CreateEvents(msg.MaxCount),
			currentPos: TFPos.Invalid,
			tfLastCommitPosition: TFPos.Invalid.CommitPosition,
			isEndOfStream: true,
			error: null);

		return ValueTask.FromResult(result);
	}

	private static IReadOnlyList<ResolvedEvent> CreateEvents(int numEvents) {
		List<ResolvedEvent> result = [];
		for (var i = 0; i < numEvents; i++)
			result.Add(ResolvedEvent.EmptyEvent);

		return result;
	}
}
