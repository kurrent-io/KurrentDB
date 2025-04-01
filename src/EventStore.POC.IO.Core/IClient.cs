// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.POC.IO.Core;

public readonly record struct Position(ulong CommitPosition, ulong PreparePosition) {
	public static readonly Position Start = new(0, 0);

	public static readonly Position End = new(ulong.MaxValue, ulong.MaxValue);

	public static bool operator <(Position a, Position b) {
		if (a.CommitPosition != b.CommitPosition) {
			return a.CommitPosition < b.CommitPosition;
		}

		return a.PreparePosition < b.PreparePosition;
	}

	public static bool operator >(Position p1, Position p2) => p2 < p1;
}

// This exists to prevent people constructing a position when subscribing to all and expecting
// to receive the event at that position in their subscription. The subscribe call is expecting
// a checkpoint of the last received event and the `After` method makes that intuitive
public readonly record struct FromAll(Position? Position) {
	public static readonly FromAll Start = new(null);

	public static readonly FromAll End = new(IO.Core.Position.End);

	public static FromAll After(Position position) => new(position);
}

public static class ExpectedVersion {
	public const long Any = -2;
	public const long NoStream = -1;
}

public interface IClient {
	//qq consider what api we actually want here
	// and clearly define the semantics such that we get the same behaviour from internal and external
	// wrt exception handling etc.

	Task WriteMetaDataMaxCountAsync(string stream, CancellationToken cancellationToken);
	Task<long> WriteAsync(string stream, EventToWrite[] events, long expectedVersion, CancellationToken cancellationToken);
	IAsyncEnumerable<Event> SubscribeToAll(FromAll start, CancellationToken cancellationToken);
	IAsyncEnumerable<Event> SubscribeToStream(string stream, CancellationToken cancellationToken);
	IAsyncEnumerable<Event> ReadStreamForwards(string stream, long maxCount, CancellationToken cancellationToken);
	IAsyncEnumerable<Event> ReadStreamBackwards(string stream, long maxCount, CancellationToken cancellationToken);
	IAsyncEnumerable<Event> ReadAllBackwardsAsync(Position position, long maxCount, CancellationToken cancellationToken);
	Task DeleteStreamAsync(string stream, long expectedVersion, CancellationToken cancellationToken);

	async Task<long> WriteAsyncRetry(string stream, EventToWrite[] events, long expectedVersion,
		CancellationToken cancellationToken, int maxRetries = 50) {

		ResponseException? lastException = null;
		for (var i = 0; i < maxRetries; i++) {
			try {
				return await WriteAsync(stream, events, expectedVersion, cancellationToken);
			} catch (ResponseException e) {
				switch (e) {
					case ResponseException.ServerBusy:
					case ResponseException.ServerNotReady:
					case ResponseException.Timeout:
						break;
					default:
						throw;
				}

				lastException = e;
				await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
			}
		}

		throw lastException!;
	}

	class None : IClient {
		public IAsyncEnumerable<Event> SubscribeToAll(FromAll start, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}

		public IAsyncEnumerable<Event> SubscribeToStream(string stream, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}

		public IAsyncEnumerable<Event> ReadStreamForwards(string stream, long maxCount, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}

		public IAsyncEnumerable<Event> ReadStreamBackwards(string stream, long maxCount, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}

		public IAsyncEnumerable<Event> ReadAllBackwardsAsync(Position position, long maxCount, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}

		public Task DeleteStreamAsync(string stream, long expectedVersion, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}

		public Task WriteMetaDataMaxCountAsync(string stream, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}

		public Task<long> WriteAsync(string stream, EventToWrite[] events, long expectedVersion, CancellationToken cancellationToken) {
			throw new System.NotImplementedException();
		}
	}
}
