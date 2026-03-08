// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class PartitionDispatcher {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionDispatcher>();

	private readonly Channel<PartitionEvent>[] _partitionChannels;
	private readonly int _partitionCount;
	private readonly Func<ResolvedEvent, string?> _getPartitionKey;
	private ulong _nextCheckpointSequence;

	/// <summary>
	/// Creates a dispatcher that computes the partition key on the read loop thread
	/// and routes events to partition channels by hash of the partition key.
	/// </summary>
	public PartitionDispatcher(
		int partitionCount,
		Func<ResolvedEvent, string?> getPartitionKey,
		int channelCapacity = 256) {
		_partitionCount = partitionCount;
		_getPartitionKey = getPartitionKey;

		_partitionChannels = new Channel<PartitionEvent>[partitionCount];
		for (int i = 0; i < partitionCount; i++) {
			_partitionChannels[i] = Channel.CreateBounded<PartitionEvent>(
				new BoundedChannelOptions(channelCapacity) {
					FullMode = BoundedChannelFullMode.Wait,
					SingleReader = true,
					SingleWriter = true
				});
		}
	}

	public ChannelReader<PartitionEvent> GetPartitionReader(int partitionIndex)
		=> _partitionChannels[partitionIndex].Reader;

	public async ValueTask DispatchEvent(ResolvedEvent @event, TFPos logPosition, CancellationToken ct) {
		var partitionKey = _getPartitionKey(@event);
		if (partitionKey is null) {
			Log.Debug("Skipping event (partition key is null) at {LogPosition}", logPosition);
			return;
		}

		var partitionIndex = GetPartitionIndex(partitionKey);
		var pe = PartitionEvent.ForEvent(@event, partitionKey, logPosition);
		await _partitionChannels[partitionIndex].Writer.WriteAsync(pe, ct);
	}

	public async ValueTask DispatchPartitionDeleted(string partitionKey, TFPos logPosition, CancellationToken ct) {
		var partitionIndex = GetPartitionIndex(partitionKey);
		var pe = PartitionEvent.ForPartitionDeleted(partitionKey, logPosition);
		await _partitionChannels[partitionIndex].Writer.WriteAsync(pe, ct);
	}

	public async ValueTask<ulong> InjectCheckpointMarker(TFPos logPosition, CancellationToken ct) {
		var sequence = ++_nextCheckpointSequence;
		var marker = PartitionEvent.ForCheckpointMarker(sequence, logPosition);

		Log.Debug("Injecting checkpoint marker {Sequence} at {LogPosition}", sequence, logPosition);

		for (int i = 0; i < _partitionCount; i++) {
			await _partitionChannels[i].Writer.WriteAsync(marker, ct);
		}

		return sequence;
	}

	public void Complete(Exception? ex = null) {
		for (int i = 0; i < _partitionCount; i++) {
			_partitionChannels[i].Writer.TryComplete(ex);
		}
	}

	private int GetPartitionIndex(string key) {
		if (_partitionCount == 1) return 0;
		var hash = XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(key));
		return (int)(hash % (uint)_partitionCount);
	}
}
