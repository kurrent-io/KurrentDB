// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Services;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.Scavenging;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

public class StreamMetadatas {
	public static StreamMetadata TruncateBefore1 { get; } = new StreamMetadata(truncateBefore: 1);
	public static StreamMetadata TruncateBefore2 { get; } = new StreamMetadata(truncateBefore: 2);
	public static StreamMetadata TruncateBefore3 { get; } = new StreamMetadata(truncateBefore: 3);
	public static StreamMetadata TruncateBefore4 { get; } = new StreamMetadata(truncateBefore: 4);

	public static StreamMetadata MaxCount1 { get; } = new StreamMetadata(maxCount: 1);
	public static StreamMetadata MaxCount2 { get; } = new StreamMetadata(maxCount: 2);
	public static StreamMetadata MaxCount3 { get; } = new StreamMetadata(maxCount: 3);
	public static StreamMetadata MaxCount4 { get; } = new StreamMetadata(maxCount: 4);
	public static StreamMetadata MaxCount50 { get; } = new StreamMetadata(maxCount: 50);

	public static TimeSpan MaxAgeTimeSpan { get; } = TimeSpan.FromDays(2);
	public static StreamMetadata MaxAgeMetadata { get; } =
		new StreamMetadata(maxAge: MaxAgeTimeSpan);

	public static StreamMetadata SoftDelete { get; } =
		new StreamMetadata(truncateBefore: EventNumber.DeletedStream);

	public static DateTime EffectiveNow { get; } = new DateTime(2022, 1, 5, 00, 00, 00);
	public static DateTime Expired { get; } = EffectiveNow - TimeSpan.FromDays(3);
	public static DateTime Cutoff { get; } = EffectiveNow - MaxAgeTimeSpan;
	public static DateTime Active { get; } = EffectiveNow - TimeSpan.FromDays(1);

	public static Rec ScavengePointRec(int transaction, int threshold = 0, DateTime? timeStamp = null) => Rec.Write(
		transaction: transaction,
		stream: SystemStreams.ScavengePointsStream,
		eventType: SystemEventTypes.ScavengePoint,
		timestamp: timeStamp ?? EffectiveNow,
		data: new ScavengePointPayload {
			Threshold = threshold,
		}.ToJsonBytes());

	public static ScavengePoint ScavengePoint(int chunk, long eventNumber) => new ScavengePoint(
		position: PositionOfChunk(chunk),
		eventNumber: eventNumber,
		effectiveNow: EffectiveNow,
		threshold: 0);

	public const long ChunkSize = 1024 * 1024;
	public static long PositionOfChunk(int logicalChunkNumber) => ChunkSize * logicalChunkNumber;
}
