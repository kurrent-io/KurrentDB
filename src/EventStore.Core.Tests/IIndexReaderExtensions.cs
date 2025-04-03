// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Tests;

public static class IIndexReaderExtensions {
	public static ValueTask<IndexReadEventResult> ReadEvent(this IIndexReader<string> index, string streamName, long eventNumber, CancellationToken token) =>
		index.ReadEvent(streamName, streamName, eventNumber, token);

	public static ValueTask<IndexReadStreamResult> ReadStreamEventsBackward(this IIndexReader<string> index, string streamName, long fromEventNumber, int maxCount, CancellationToken token) =>
		index.ReadStreamEventsBackward(streamName, streamName, fromEventNumber, maxCount, token);

	public static ValueTask<IndexReadStreamResult> ReadStreamEventsForward(this IIndexReader<string> index, string streamName, long fromEventNumber, int maxCount, CancellationToken token) =>
		index.ReadStreamEventsForward(streamName, streamName, fromEventNumber, maxCount, token);
}
