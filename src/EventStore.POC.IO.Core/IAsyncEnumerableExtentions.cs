// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EventStore.POC.IO.Core;

public static class IAsyncEnumerableExtentions {
	public static async Task ToChannelAsync<T>(
		this IAsyncEnumerable<T> self,
		ChannelWriter<T> writer,
		CancellationToken ct) {

		await foreach (var x in self.WithCancellation(ct)) {
			await writer.WriteAsync(x, ct);
		}
	}

	public static async IAsyncEnumerable<T> HandleStreamNotFound<T>(
		this IAsyncEnumerable<T> self) {

		await Task.Yield();

		await using var enumerator = self.GetAsyncEnumerator();
		while (true) {
			try {
				if (!await enumerator.MoveNextAsync())
					break;
			} catch (ResponseException.StreamNotFound) {
				break;
			}

			yield return enumerator.Current;
		}
	}
}
