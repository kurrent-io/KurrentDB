// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Threading;
using EventStore.Core.TransactionLog.Scavenging;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge;

public class ParallelLoopTests {
	static async Task RunAsync(
		int[] source,
		int[] completionOrder,
		int[] expectedCheckpoints,
		int degreeOfParallelism = 2) {

		if (source.Any(x => x % 10 != 0))
			throw new Exception("use multiples of 10");

		// maps from the item to the mres it should wait for
		var selfTriggers = new Dictionary<int, AsyncManualResetEvent>();
		// maps from item to the mres it should trigger when it completes
		var nextTriggers = new Dictionary<int, AsyncManualResetEvent>();

		var prev = default(int?);
		foreach (var item in completionOrder) {
			selfTriggers[item] = new AsyncManualResetEvent(initialState: false);
			if (prev is not null)
				nextTriggers[prev.Value] = selfTriggers[item];

			prev = item;
		}

		if (completionOrder.Length > 0) {
			nextTriggers[completionOrder.Last()] = new AsyncManualResetEvent(initialState: false);
			selfTriggers[completionOrder[0]].Set();
		}

		// make sure only one item completes at a time, to force the sut to always pick the same slot
		var serializer = new AsyncManualResetEvent(initialState: true);
		var emittedCheckpoints = new List<int>();
		var completedItems = new List<int>();

		await ParallelLoop.RunWithTrailingCheckpointAsync(
			source: source.ToAsyncEnumerable(),
			degreeOfParallelism: degreeOfParallelism,
			getCheckpointInclusive: x => x,
			getCheckpointExclusive: x => {
				var chunkStartNumber = x == 10
					? x - 10
					: x - 9;
				if (chunkStartNumber == 00)
					return null;
				return chunkStartNumber - 1;
			},
			process: async (slot, x, token) => {

				// wait until we are complete
				await selfTriggers[x].WaitAsync(token);
				completedItems.Add(x);

				await serializer.WaitAsync(token);
				serializer.Reset();

				// complete the next in line
				nextTriggers[x].Set();
			},
			emitCheckpoint: checkpoint => {
				emittedCheckpoints.Add(checkpoint);
			},
			onConsiderEmit: () => serializer.Set());


		Assert.Equal(completionOrder, completedItems);
		Assert.Equal(expectedCheckpoints, emittedCheckpoints);
	}

	[Fact]
	public async Task process_out_of_order_async() => await RunAsync(
		source: [10, 20, 30, 40, 50],
		completionOrder: [20, 30, 40, 10, 50],
		expectedCheckpoints: [40, 50]);

	[Fact]
	public async Task process_very_out_of_order_async() => await RunAsync(
		source: [10, 20, 30, 40, 50],
		completionOrder: [20, 30, 40, 50, 10],
		expectedCheckpoints: [50]);

	[Fact]
	public async Task process_interleaved_async() => await RunAsync(
		source: [10, 20, 30, 40, 50],
		completionOrder: [20, 10, 30, 40, 50],
		expectedCheckpoints: [20, 30, 40, 50]);

	[Fact]
	public async Task one_degree_of_parallelism() => await RunAsync(
		source: [10, 20, 30],
		completionOrder: [10, 20, 30],
		expectedCheckpoints: [10, 20, 30],
		degreeOfParallelism: 1);

	[Fact]
	public async Task four_degrees_of_parallelism_async() => await RunAsync(
		source: [10, 20, 30, 40, 50],
		completionOrder: [10, 20, 30, 40, 50],
		expectedCheckpoints: [10, 20, 30, 40, 50],
		degreeOfParallelism: 4);

	[Fact]
	public async Task same_degress_as_elements_async() => await RunAsync(
		source: [10, 20],
		completionOrder: [10, 20],
		expectedCheckpoints: [10, 20]);

	[Fact]
	public async Task same_degress_as_elements_out_of_order() => await RunAsync(
		source: [10, 20],
		completionOrder: [20, 10],
		expectedCheckpoints: [20]);


	[Fact]
	public async Task more_degress_than_elements() => await RunAsync(
		source: [10],
		completionOrder: [10],
		expectedCheckpoints: [10]);

	[Fact]
	public async Task empty_source() => await RunAsync(
		source: [],
		completionOrder: [],
		expectedCheckpoints: []);

	[Fact]
	public async Task exception_during_processing_is_propagated_async() {
		var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
			await ParallelLoop.RunWithTrailingCheckpointAsync(
				source: new[] { 10 }.ToAsyncEnumerable(),
				degreeOfParallelism: 2,
				getCheckpointInclusive: x => x,
				getCheckpointExclusive: x => x,
				process: (slot, x, token) => Task.FromException(new InvalidOperationException("something went wrong")),
				emitCheckpoint: checkpoint => {
				});
		});

		Assert.Equal("something went wrong", ex.Message);
	}

	[Fact]
	public async Task exception_during_emit_is_propagated_async() {
		var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
			await ParallelLoop.RunWithTrailingCheckpointAsync(
				source: new[] { 10 }.ToAsyncEnumerable(),
				degreeOfParallelism: 2,
				getCheckpointInclusive: x => x,
				getCheckpointExclusive: x => x,
				process: (slot, x, token) => Task.CompletedTask,
				emitCheckpoint: checkpoint => throw new InvalidOperationException("something went wrong")
			);
		});

		Assert.Equal("something went wrong", ex.Message);
	}
}
